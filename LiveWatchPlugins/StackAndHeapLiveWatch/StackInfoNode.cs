using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualGDBExtensibility.LiveWatch;

namespace StackAndHeapLiveWatch
{
    class StackInfoNode : ScalarNodeBase
    {
        readonly ILiveWatchEngine _Engine;
        readonly ulong _StackStart, _StackEnd;
        bool _StackOpposesGrowingHeap;
        readonly string _Error;

        ILiveVariable _BorderVariable;

        uint _UnusedStackFillPatern;
        bool _OverflowDetected, _PatternEverFound;

        int _MaxBorderVariableSize;
        private ILiveVariable _HeapEndVariable;

        public StackInfoNode(ILiveWatchEngine engine)
            : base("$stack")
        {
            _Engine = engine;
            SelectedFormatter = engine.CreateDefaultFormatter(ScalarVariableType.SInt32);
            _UnusedStackFillPatern = engine.Settings.UnusedStackFillPattern;
            _MaxBorderVariableSize = engine.Settings.StackBorderWatchSize;

            Name = "Highest Stack Usage";
            Capabilities |= LiveWatchCapabilities.CanHaveChildren;

            try
            {
                var endOfStackVariable = engine.Evaluator.TryLookupRawSymbolInfo("_estack") ?? engine.Evaluator.TryLookupRawSymbolInfo("__StackLimit") ?? throw new Exception("No '_estack' or '__StackLimit' symbol found.");
                _StackEnd = endOfStackVariable.Address;

                var reservedForStackVariable = engine.Evaluator.LookupVariable("ReservedForStack");
                if (reservedForStackVariable != null && reservedForStackVariable.Size != 0)
                {
                    //Stack size is fixed. No need to monitor outside it.
                    _StackStart = _StackEnd - (uint)reservedForStackVariable.Size;
                }
                else
                {
                    //Stack size is variable. Initially, it starts right after the 'end' symbol, but can be moved further as the heap grows.
                    var endVariable = engine.Evaluator.TryLookupRawSymbolInfo("end") ?? throw new Exception("No 'end' symbol found");
                    var heapEndVariableAddress = engine.Evaluator.FindSymbolsContainingString("heap_end").SingleOrDefault().Address;
                    if (heapEndVariableAddress != 0)
                        _HeapEndVariable = engine.LiveVariables.CreateLiveVariable(heapEndVariableAddress, 4);

                    _StackStart = endVariable.Address;
                    _StackOpposesGrowingHeap = true;
                }
            }
            catch (Exception ex)
            {
                _Error = ex.Message;
            }
        }

        public override void Dispose()
        {
            _HeapEndVariable.Dispose();
            _BorderVariable?.Dispose();
            base.Dispose();
        }

        int CountUnusedStackMarkers(byte[] data)
        {
            int offset = 0;
            while (offset < (data.Length - 3))
            {
                uint value = BitConverter.ToUInt32(data, offset);
                if (value != _UnusedStackFillPatern)
                    return offset;
                offset += 4;
            }
            return offset; 
        }

        public override void SetSuspendState(LiveWatchNodeSuspendState state)
        {
            if (_BorderVariable != null)
                _BorderVariable.SuspendUpdating = state.SuspendRegularUpdates;
            if (_HeapEndVariable != null)
                _HeapEndVariable.SuspendUpdating = state.SuspendRegularUpdates;

            base.SetSuspendState(state);
        }

        ILiveWatchNode[] _Children;
        private LiveVariableValue _DistanceToHeapEnd;

        class DistanceToHeapNode : ScalarNodeBase
        {
            private StackInfoNode _StackNode;

            public DistanceToHeapNode(StackInfoNode stackInfo) 
                : base("$stack.distance")
            {
                _StackNode = stackInfo;
                Name = "Distance to end of heap";
                SelectedFormatter = stackInfo._Engine.CreateDefaultFormatter(ScalarVariableType.SInt32);
            }

            public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
            {
                RawValue = _StackNode._DistanceToHeapEnd;

                return new LiveWatchNodeState
                {
                    Value = SelectedFormatter.FormatValue(RawValue.Value)
                };
            }
        }

        public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            int estimatedStackSize = (int)(_StackEnd - _StackStart);

            if (_OverflowDetected)
                return ReportStackOverflow(estimatedStackSize);

            if (_Error != null)
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = _Error };

            var rawValue = _BorderVariable?.GetValue() ?? default;

            if (!rawValue.IsValid || CountUnusedStackMarkers(rawValue.Value) != rawValue.Value.Length)
            {
                ulong lastKnownEndOfStack;
                if (_BorderVariable != null)
                    lastKnownEndOfStack = _BorderVariable.Address + (uint)_BorderVariable.Size;
                else
                    lastKnownEndOfStack = _StackEnd;

                _BorderVariable?.Dispose();
                _BorderVariable = null;

                ulong startOfCheckedArea;
                if (!_StackOpposesGrowingHeap)
                {
                    //Maximum size is fixed. No guessing needed.
                    startOfCheckedArea = _StackStart;
                }
                else if (_HeapEndVariable != null)
                {
                    //Stack immediately follows the dynamically growing heap
                    startOfCheckedArea = _HeapEndVariable.GetValue().ToUlong();
                    if (startOfCheckedArea == 0)
                        startOfCheckedArea = _StackStart;   //The heap has never been used yet
                    estimatedStackSize = (int)(_StackEnd - _StackStart);
                }
                else
                {
                    //No heap. Stack directly follows the 'end' symbol.
                    startOfCheckedArea = _StackStart;
                }

                int position = MeasureUnusedStackArea(startOfCheckedArea, lastKnownEndOfStack);
                if (position == 0)
                    _OverflowDetected = true;
                else
                    _PatternEverFound = true;

                if (_OverflowDetected)
                    return ReportStackOverflow(estimatedStackSize);
                else
                {
                    int watchSize = Math.Min(_MaxBorderVariableSize, position);
                    _BorderVariable = _Engine.LiveVariables.CreateLiveVariable(startOfCheckedArea + (uint)position - (uint)watchSize, watchSize);
                }
            }

            ulong firstUsedStackSlot = _BorderVariable.Address + (uint)_BorderVariable.Size;
            Location = new LiveWatchPhysicalLocation(firstUsedStackSlot, null, 0);
            int stackUsage = (int)(_StackEnd - firstUsedStackSlot);
            RawValue = new LiveVariableValue(rawValue.Timestamp, rawValue.Generation, BitConverter.GetBytes(stackUsage));

            if (_HeapEndVariable != null)
            {
                var rawHeapEnd = _HeapEndVariable.GetValue();
                ulong heapEnd = rawHeapEnd.ToUlong();
                if (heapEnd == 0)
                    heapEnd = _StackStart;

                estimatedStackSize = (int)(_StackEnd - heapEnd);

                _DistanceToHeapEnd = new LiveVariableValue(rawHeapEnd.Timestamp, rawHeapEnd.Generation, BitConverter.GetBytes((int)(firstUsedStackSlot - heapEnd)));
            }

            string text;
            if (estimatedStackSize > 0)
                text = $"{stackUsage}/{estimatedStackSize} bytes";
            else
                text = $"{stackUsage} bytes";

            if (context.PreloadChildren && _Children == null && _HeapEndVariable != null)
            {
                _Children = new ILiveWatchNode[] { new DistanceToHeapNode(this) };
            }

            return new LiveWatchNodeState
            {
                Value = text,
                Icon = LiveWatchNodeIcon.Stack,
                NewChildren = _Children,
            };
        }

        private int MeasureUnusedStackArea(ulong startOfCheckedArea, ulong lastKnownEndOfStack)
        {
            const int checkChunkSize = 2048;
            int markerBytes = 0;

            for (ulong uncheckedChunkStart = lastKnownEndOfStack - checkChunkSize; uncheckedChunkStart >= startOfCheckedArea; uncheckedChunkStart -= checkChunkSize)
            {
                ulong chunkStart = Math.Max(uncheckedChunkStart, startOfCheckedArea);
                int checkSize = Math.Min(checkChunkSize, (int)(lastKnownEndOfStack - chunkStart));

                var data = _Engine.LiveVariables.ReadMemory(chunkStart, checkChunkSize);
                if (!data.IsValid)
                    throw new Exception($"Failed to read stack memory (0x{startOfCheckedArea:x8}-0x{lastKnownEndOfStack})");

                markerBytes = CountUnusedStackMarkers(data.Value);
                if (markerBytes > (checkChunkSize / 2))
                {
                    //More than the half of the chunk is filled. For performance reasons, we do not recheck the rest.
                    int chunkBase = (int)(chunkStart - startOfCheckedArea);
                    return chunkBase + markerBytes;
                }
            }

            return markerBytes;
        }

        private LiveWatchNodeState ReportStackOverflow(int estimatedStackSize)
        {
            if (!_PatternEverFound)
                _Engine.ReportConfigurationError(LiveWatchConfigurationError.UnusedStackNotFilledWithPattern);

            RawValue = new LiveVariableValue(DateTime.Now, LiveVariableValue.OutOfScheduleGeneration, BitConverter.GetBytes(estimatedStackSize));
            return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = _PatternEverFound ? "Stack overflow detected!" : $"Unused stack is not filled with 0x{_UnusedStackFillPatern}" };
        }

    }
}
