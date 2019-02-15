using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VisualGDBExtensibility;

namespace ARMStackUsageAnalyzer
{
    public struct StackAnalyzerContext
    {
        public int Depth;
        public int SavedStackDepth;
        public bool FramePointedChangedUnpredictably;

        public int EntryStackDepthForLocalCalls;

        public StackAnalyzerContext Clone() => new StackAnalyzerContext
        {
            Depth = Depth,
            SavedStackDepth = SavedStackDepth,
            FramePointedChangedUnpredictably = FramePointedChangedUnpredictably,
            EntryStackDepthForLocalCalls = EntryStackDepthForLocalCalls,
        };

        public override string ToString()
        {
            return $"Stack Depth = {Depth}";
        }
    }

    public struct PendingCodePath
    {
        public readonly ulong Address;
        public readonly StackAnalyzerContext Context;

        public PendingCodePath(ulong address, StackAnalyzerContext context)
        {
            Address = address;
            Context = context;
        }

        public override string ToString()
        {
            return $"0x{Address:x8} with {Context}";
        }
    }


    [Flags]
    public enum StackRelatedInstructionEffect
    {
        None = 0,
        MovesStackPointer = 1,
        SavesStackPointerWithDelta = 2,
        RestoresStackPointer = 4,
        ConditionalJump = 8,
        UnconditionalJump = 16,
        ChangesStackPointerUnpredictably = 32,
        FunctionCall = 64,
        ReturnFromCall = 128,
        UnrecognizableInstruction = 256,
        UnpredictableJump = 512,
        MovesSavedStackPointer = 1024,
        RegisterJump = 2048,
        JumpTargetKnown = 4096,
        ChangesFramePointerUnpredictably = 8192,
        JumpsViaLinkRegister = 16384,

        WarningMask = ChangesStackPointerUnpredictably | UnrecognizableInstruction | UnpredictableJump | RegisterJump,
    }



    public struct StackRelatedInstructionEffects
    {
        public StackRelatedInstructionEffect Effects;
        public int StackDelta;
        public ulong JumpTarget;

        public bool HasAnyEffect(StackRelatedInstructionEffect effect) => (Effects & effect) != StackRelatedInstructionEffect.None;

        public override string ToString()
        {
            string result = Effects.ToString();
            if (StackDelta != 0)
                result += $" [StackDelta = {StackDelta}]";
            if (HasAnyEffect(StackRelatedInstructionEffect.JumpTargetKnown))
                result += $" [Target = 0x{JumpTarget:x8}]";
            return result;
        }
    }


    class ARMStackUsageAnalyzer : IPlatformSpecificStackAnalyzer
    {
        private readonly IStackUsageAnalyzerHost _Host;

        public ARMStackUsageAnalyzer(IStackUsageAnalyzerHost host)
        {
            _Host = host;
        }

        public FunctionStackUsage AnalyzeFunctionStackUsage(AnalyzedSymbol function, IStackAnalyzerLogger optionalLogger)
        {
            FunctionStackUsage result = new FunctionStackUsage { CalledFunctions = new List<CalledFunctionStackUsage>() };

            var codePaths = new Queue<PendingCodePath>();
            codePaths.Enqueue(new PendingCodePath(function.Address, new StackAnalyzerContext()));

            HashSet<ulong> coveredAddresses = new HashSet<ulong>();

#if DEBUG
            int codePathNumber = 0;
#endif

            while (codePaths.Count > 0)
            {
#if DEBUG
                codePathNumber++;
#endif
                var path = codePaths.Dequeue();
                var ctx = path.Context;

                optionalLogger?.LogLine($"***Starting code path at 0x{path.Address:x8} with stack depth = {path.Context.Depth}" + (path.Context.EntryStackDepthForLocalCalls != 0 ? $" (local subroutine with a stack offset of {path.Context.EntryStackDepthForLocalCalls})" : ""));

                foreach (var insn in _Host.TryReadInstructions(path.Address))
                {
                    if (insn.Opcode == null)
                    {
                        result.AppendFlag(FunctionStackUsageFlags.HasJumpsToUnreadableAddresses, insn, optionalLogger);
                        break;
                    }

                    if (coveredAddresses.Contains(insn.Address))
                    {
                        optionalLogger?.LogLine($"*** 0x{path.Address:x8} already analyzed");
                        break;  //Already checked this instruction from another path. We may want to double-check that the stack depth is the same as last time though.
                    }

                    coveredAddresses.Add(insn.Address);

                    var effects = ClassifyInstruction(insn);

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.ChangesStackPointerUnpredictably))
                        result.AppendFlag(FunctionStackUsageFlags.HasDynamicStack, insn, optionalLogger);
                    else if (effects.HasAnyEffect(StackRelatedInstructionEffect.RegisterJump))
                        result.AppendFlag(FunctionStackUsageFlags.HasDynamicCalls, insn, optionalLogger);
                    else if (effects.HasAnyEffect(StackRelatedInstructionEffect.WarningMask))
                        result.AppendFlag(FunctionStackUsageFlags.HasOtherWarning, insn, optionalLogger);

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.SavesStackPointerWithDelta))
                        ctx.SavedStackDepth = ctx.Depth + effects.StackDelta;
                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.RestoresStackPointer))
                    {
                        ctx.Depth = ctx.SavedStackDepth;
                        if (ctx.FramePointedChangedUnpredictably)
                            result.AppendFlag(FunctionStackUsageFlags.HasDynamicStack, insn, optionalLogger);
                    }
                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.ChangesFramePointerUnpredictably))
                        ctx.FramePointedChangedUnpredictably = true;

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.MovesSavedStackPointer))
                        ctx.SavedStackDepth += effects.StackDelta;

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.MovesStackPointer))
                    {
                        result.MaximumOwnDepthIncludingPushedArguments = Math.Max(result.MaximumOwnDepthIncludingPushedArguments, ctx.Depth);
                        ctx.Depth += effects.StackDelta;
                        result.MaximumOwnDepthIncludingPushedArguments = Math.Max(result.MaximumOwnDepthIncludingPushedArguments, ctx.Depth);
                        if (ctx.Depth < 0)
                        {
                            optionalLogger?.ReportWarning(insn, FunctionStackUsageFlags.HasStackImbalance);
                            result.AppendFlag(FunctionStackUsageFlags.HasStackUnderrun, insn, optionalLogger);
                        }
                    }

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.FunctionCall))
                    {
                        bool isLocalCall = false;
                        if (effects.HasAnyEffect(StackRelatedInstructionEffect.JumpTargetKnown))
                        {
                            if (function.ContainsAddress(insn.Address))
                            {
                                //This is a local subroutine (e.g. used by __aeabi_dmul). Once it reaches the 'bx lr' instruction, it will get back to the next instruction after the current one.
                                isLocalCall = true;
                                var ctx2 = ctx.Clone();
                                ctx2.EntryStackDepthForLocalCalls = ctx2.Depth;
                                codePaths.Enqueue(new PendingCodePath(effects.JumpTarget, ctx2));
                            }
                            else
                                result.CalledFunctions.Add(new CalledFunctionStackUsage(insn.Address, effects.JumpTarget, ctx.Depth, false));
                        }

                        if (!isLocalCall && _Host.IsNoreturnFunction(effects.JumpTarget))
                            break;  //This call will never return
                    }

                    optionalLogger?.ReportInstructionStatus(ctx.Depth, insn, effects.ToString());

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.ReturnFromCall))
                    {
                        //End of path. This is either a return from the function, or a return from a local subroutine.
                        if (ctx.EntryStackDepthForLocalCalls != 0 && effects.HasAnyEffect(StackRelatedInstructionEffect.JumpsViaLinkRegister))
                        {
                            if (ctx.Depth != ctx.EntryStackDepthForLocalCalls)
                                result.AppendFlag(FunctionStackUsageFlags.HasStackImbalance, insn, optionalLogger);
                        }
                        else
                        {
                            if (ctx.Depth != 0)
                                result.AppendFlag(FunctionStackUsageFlags.HasStackImbalance, insn, optionalLogger);
                        }

                        break;                                           
                    }

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.UnconditionalJump) && ctx.Depth == 0 && !function.ContainsAddress(effects.JumpTarget))
                    {
                        //This is a tail call at the end of the function
                        if (effects.HasAnyEffect(StackRelatedInstructionEffect.JumpTargetKnown))
                            result.CalledFunctions.Add(new CalledFunctionStackUsage(insn.Address, effects.JumpTarget, ctx.Depth, true));
                        break;
                    }

                    if (effects.HasAnyEffect(StackRelatedInstructionEffect.ConditionalJump | StackRelatedInstructionEffect.UnconditionalJump))
                    {
                        if (effects.HasAnyEffect(StackRelatedInstructionEffect.JumpTargetKnown))
                            codePaths.Enqueue(new PendingCodePath(effects.JumpTarget, ctx.Clone()));

                        if (effects.HasAnyEffect(StackRelatedInstructionEffect.UnconditionalJump))
                            break;  //We will continue this when we start analyzing the queued path.
                    }
                }
            }

            return result;
        }

        #region Auxiliary methods
        Regex rgRegister = new Regex("^[a-z][a-z0-9.]+$");
        Regex rgSpRelativeAddressing = new Regex(@"\[sp, *#(-?[0-9]+)\]!");

        bool IsSingleRegister(string op) => rgRegister.IsMatch(op);

        string TakeFirstArgument(ref string remainingArgs, char separator = ',')
        {
            int idx = remainingArgs.IndexOf(separator);
            string result;
            if (idx == -1)
            {
                result = remainingArgs;
                remainingArgs = "";
                return result;
            }
            else
            {
                result = remainingArgs.Substring(0, idx).Trim();
                remainingArgs = remainingArgs.Substring(idx + 1).Trim();
                return result;
            }
        }

        int? ParseImmediateValue(string value)
        {
            if (value?.StartsWith("#") != true)
                return null;
            if (int.TryParse(value.Substring(1), out var parsedValue))
                return parsedValue;
            else
                return null;
        }

        ulong? ParseAddress(string value)
        {
            if (value?.StartsWith("0x") == true)
                value = value.Substring(2); //GDB prefixes the addresses with "0x", objdump doesn't

            if (ulong.TryParse(value, NumberStyles.AllowHexSpecifier, null, out var parsedValue))
                return parsedValue;
            else
                return null;
        }

        private ulong StripThumbBit(ulong value) => value & ~1UL;

        static readonly string[] ARMConditionCodes = { "eq", "ne", "cs", "cc", "mi", "pl", "vs", "vc", "hi", "ls", "ge", "lt", "gt", "le", "al" };

        int GetRegisterListLength(string registerList, out bool includesPc)
        {
            registerList = registerList.Trim();
            includesPc = false;
            if (registerList.StartsWith("{"))
            {
                string[] pushedArgs = registerList.Trim('{', '}').Split(',');
                foreach (var arg in pushedArgs.Select(a => a.Trim()))
                {
                    if (!IsSingleRegister(arg))
                        throw new ARMStackUsageAnalyzerException("Unexpected push/pop argument format: " + registerList);

                    if (arg == "pc")
                        includesPc = true;
                }

                return pushedArgs.Length;
            }
            else
            {
                if (!IsSingleRegister(registerList))
                    throw new ARMStackUsageAnalyzerException("Unexpected push/pop argument format: " + registerList);

                if (registerList == "pc")
                    includesPc = true;

                return 1;
            }
        }

        int GetRegisterListLength(string registerList) => GetRegisterListLength(registerList, out bool x);

        #endregion

        public class ARMStackUsageAnalyzerException : Exception
        {
            public ARMStackUsageAnalyzerException(string message)
                : base(message)
            {
            }
        }

        const int WordSize = 4;

        StackRelatedInstructionEffects ClassifyInstruction(StackAnalyzerInstruction insn)
        {
            string remainingArgs = insn.Arguments;

            if (insn.Opcode == "push" || insn.Opcode == "pop")
            {
                int sign = insn.Opcode == "push" ? 1 : -1;
                int listLength = GetRegisterListLength(insn.Arguments, out var hasPC);
                StackRelatedInstructionEffect extraEffects = StackRelatedInstructionEffect.None;
                if (hasPC)
                    extraEffects |= StackRelatedInstructionEffect.ReturnFromCall;

                return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.MovesStackPointer | extraEffects, StackDelta = sign * WordSize * listLength };
            }
            else if (insn.Opcode.StartsWith("mov"))
            {
                string arg = TakeFirstArgument(ref remainingArgs);
                string arg2 = TakeFirstArgument(ref remainingArgs);
                if (arg == "sp")
                {
                    if (arg2 == "r7")
                        return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.RestoresStackPointer };
                    else
                        return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ChangesStackPointerUnpredictably };
                }
                else if (arg == "r7" && arg2 == "sp")
                {
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.SavesStackPointerWithDelta };
                }
            }
            else if (insn.Opcode == "ldr" || insn.Opcode.StartsWith("ldr.w") || insn.Opcode == "ldrd" || insn.Opcode.StartsWith("ldrd.") ||
                     insn.Opcode == "str" || insn.Opcode.StartsWith("str.w") || insn.Opcode == "strd" || insn.Opcode.StartsWith("strd."))
            {
                string arg = TakeFirstArgument(ref remainingArgs);
                if (insn.Opcode.StartsWith("ldr") && arg == "sp")
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ChangesStackPointerUnpredictably };

                if (insn.Opcode.StartsWith("strd") || insn.Opcode.StartsWith("ldrd"))
                    TakeFirstArgument(ref remainingArgs);   //Ignore the second loaded/stored register

                Match m;

                if (remainingArgs.StartsWith("[sp,") && (m = rgSpRelativeAddressing.Match(remainingArgs)).Success)
                {
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.MovesStackPointer, StackDelta = -int.Parse(m.Groups[1].Value) };
                }
                else
                {
                    string arg2 = TakeFirstArgument(ref remainingArgs);
                    if (arg2 == "[sp]")
                    {
                        if (int.TryParse(remainingArgs.Trim('#', '!'), out int offset))
                            return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.MovesStackPointer, StackDelta = -offset };
                    }
                }
            }
            else if (insn.Opcode == "add" || insn.Opcode == "sub" || insn.Opcode == "adds" || insn.Opcode == "subs" || insn.Opcode == "addw" || insn.Opcode == "subw" || insn.Opcode == "add.w" || insn.Opcode == "sub.w")
            {
                int sign = insn.Opcode.StartsWith("sub") ? 1 : -1;

                string targetRegister = TakeFirstArgument(ref remainingArgs);
                string sourceRegister = TakeFirstArgument(ref remainingArgs);
                if (remainingArgs == "")
                {
                    remainingArgs = sourceRegister;
                    sourceRegister = targetRegister;
                }

                if (targetRegister == "sp")
                {
                    var delta = ParseImmediateValue(remainingArgs);
                    if (delta.HasValue)
                    {
                        if (targetRegister == "sp")
                            return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.MovesStackPointer, StackDelta = sign * delta.Value };
                        else if (targetRegister == "r7")
                            return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.SavesStackPointerWithDelta, StackDelta = sign * delta.Value };
                    }
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ChangesStackPointerUnpredictably };
                }
                else if (targetRegister == "r7")
                {
                    if (sourceRegister == "sp")
                    {
                        var delta = ParseImmediateValue(remainingArgs);
                        if (delta.HasValue)
                            return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.SavesStackPointerWithDelta, StackDelta = sign * delta.Value };
                    }
                    else
                    {
                        var delta = ParseImmediateValue(remainingArgs);
                        if (delta.HasValue)
                            return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.MovesSavedStackPointer, StackDelta = sign * delta.Value };
                    }

                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ChangesFramePointerUnpredictably };
                }
            }
            else if (insn.Opcode == "cbz" || insn.Opcode == "cbnz")
            {
                string arg1 = TakeFirstArgument(ref remainingArgs);
                var target = ParseAddress(remainingArgs);
                if (target.HasValue)
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ConditionalJump | StackRelatedInstructionEffect.JumpTargetKnown, JumpTarget = StripThumbBit(target.Value) };
                else
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.UnpredictableJump };
            }
            else if (insn.Opcode.StartsWith("b"))
            {
                string arg = TakeFirstArgument(ref remainingArgs, ' ');

                if (insn.Opcode == "bx" && arg == "lr")
                    return new StackRelatedInstructionEffects { Effects = StackRelatedInstructionEffect.ReturnFromCall | StackRelatedInstructionEffect.JumpsViaLinkRegister };

                StackRelatedInstructionEffect effect = StackRelatedInstructionEffect.None;

                foreach (var cc in ARMConditionCodes)
                    if (insn.Opcode.StartsWith("bl" + cc))
                    {
                        effect = StackRelatedInstructionEffect.FunctionCall;
                        break;
                    }

                if (effect == StackRelatedInstructionEffect.None)
                {
                    foreach (var cc in ARMConditionCodes)
                        if (insn.Opcode.StartsWith("b" + cc))
                        {
                            effect = StackRelatedInstructionEffect.ConditionalJump;
                            break;
                        }
                }

                if (effect == StackRelatedInstructionEffect.None)
                {
                    if (insn.Opcode.StartsWith("bl"))
                        effect = StackRelatedInstructionEffect.FunctionCall;
                    else if (insn.Opcode == "b" || insn.Opcode == "bx" || insn.Opcode.StartsWith("b.") || insn.Opcode.StartsWith("bx."))
                        effect = StackRelatedInstructionEffect.UnconditionalJump;
                }

                if (effect != StackRelatedInstructionEffect.None)
                {
                    var target = ParseAddress(arg);
                    if (target.HasValue)
                        return new StackRelatedInstructionEffects { Effects = effect | StackRelatedInstructionEffect.JumpTargetKnown, JumpTarget = StripThumbBit(target.Value) };
                    else
                    {
                        if (IsSingleRegister(arg) && remainingArgs == "")
                            return new StackRelatedInstructionEffects { Effects = effect | StackRelatedInstructionEffect.RegisterJump };
                        else
                            return new StackRelatedInstructionEffects { Effects = effect | StackRelatedInstructionEffect.UnpredictableJump };
                    }
                }
            }
            else if (insn.Opcode.StartsWith("ldmia") || insn.Opcode.StartsWith("stmdb"))
            {
                int sign = insn.Opcode.StartsWith("stmdb") ? 1 : -1;

                string arg0 = TakeFirstArgument(ref remainingArgs);
                if (arg0 == "sp!")
                {
                    var result = new StackRelatedInstructionEffects
                    {
                        Effects = StackRelatedInstructionEffect.MovesStackPointer,
                        StackDelta = sign * WordSize * GetRegisterListLength(remainingArgs, out var includesPC)
                    };

                    if (includesPC)
                        result.Effects |= StackRelatedInstructionEffect.ReturnFromCall;

                    return result;
                }
            }

            return default(StackRelatedInstructionEffects);
        }

        public void Dispose()
        {
        }

        public bool IsNoreturnFunction(AnalyzedSymbol function, IStackAnalyzerLogger optionalLogger)
        {
            return false;
        }
    }

    public class ARMStackUsageAnalyzerFactory : IPlatformSpecificStackAnalyzerFactory
    {
        public int[] ELFMachineIDs => new int[] { 0x28 };

        public string Name => "ARM";

        public IPlatformSpecificStackAnalyzer CreateAnalyzer(IStackUsageAnalyzerHost host) => new ARMStackUsageAnalyzer(host);

        public int Probe(ISimpleELFFile file)
        {
            return 1000;
        }
    }
}
