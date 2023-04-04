using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class CounterNode : IScalarLiveWatchNode
    {
        private readonly ILiveVariable _LiveVar;
        private ILiveWatchEngine _Engine;
        private IPinnedVariable _Counter;
        private ILiveVariable _NameVar;

        public ulong Address => _Counter.Address;

        public CounterNode(ILiveWatchEngine engine, IPinnedVariable counter)
        {
            _Engine = engine;
            _Counter = counter;

            var countField = counter.LookupChildRecursively("Count");
            if (countField != null)
            {
                _LiveVar = _Engine.CreateLiveVariable(countField);
            }

            if (counter.LookupChildRecursively("Name") is IPinnedVariable nameField)
                _NameVar = _Engine.CreateLiveVariable(nameField);

            SupportedFormatters = _Engine.GetFormattersForSize(4, ScalarVariableType.UInt32);
            SelectedFormatter = SupportedFormatters?.FirstOrDefault();
        }

        public string UniqueID => "counter:" + _Counter.UserFriendlyName;
        public string RawType => "Sample Counter";
        public string Name => _Counter.UserFriendlyName + " from " + Path.GetFileName(_Counter.SourceLocation.File ?? "");

        public LiveWatchCapabilities Capabilities => LiveWatchCapabilities.CanHaveChildren | LiveWatchCapabilities.CanPlotValue | LiveWatchCapabilities.CanSetBreakpoint;

        public LiveWatchPhysicalLocation Location
        {
            get
            {
                return new LiveWatchPhysicalLocation(_Counter.Address,
                    _Counter.SourceLocation.File,
                    _Counter.SourceLocation.Line);
            }
        }

        public ILiveWatchFormatter[] SupportedFormatters { get; }
        public ILiveWatchFormatter SelectedFormatter { get; set; }
        public LiveVariableValue RawValue { get; private set; }
        public LiveWatchEnumValue[] EnumValues => null;

        public ILiveWatchNode[] GetChildren(LiveWatchChildrenRequestReason reason)
        {
            var result = new List<ILiveWatchNode>();
            var node = _Engine.CreateNodeForPinnedVariable(_Counter,
                new LiveWatchNodeOverrides
                {
                    Name = "[raw object]"
                });

            if (node != null)
                result.Add(node);

            return result.ToArray();
        }

        public void SetSuspendState(LiveWatchNodeSuspendState state)
        {
            if (_LiveVar != null)
                _LiveVar.SuspendUpdating = state.SuspendRegularUpdates;
            if (_NameVar != null)
                _NameVar.SuspendUpdating = state.SuspendRegularUpdates;
        }

        public void Dispose()
        {
            _LiveVar?.Dispose();
            _NameVar?.Dispose();
        }

        public void SetValueAsString(string newValue)
        {
            throw new NotSupportedException();
        }

        ulong _LastKnownNamePtr;
        string _LastKnownName;

        public LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            var state = new LiveWatchNodeState();
            var value = RawValue = _LiveVar.GetValue();
            if (value.IsValid)
                state.Value = SelectedFormatter?.FormatValue(value.Value) ?? value.ToUlong().ToString();

            var namePtr = _NameVar.GetValue().ToUlong();
            if (namePtr != _LastKnownNamePtr)
            {
                _LastKnownNamePtr = namePtr;
                _LastKnownName = _Engine.Memory.ReadMemory(namePtr, 128).ToNullTerminatedString();
            }

            state.NewName = _LastKnownName;

            return state;
        }

        public void SetEnumValue(LiveWatchEnumValue value)
        {
            throw new NotSupportedException();
        }
    }
}