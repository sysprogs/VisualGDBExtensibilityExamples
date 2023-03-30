using System;
using System.Collections.Generic;
using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class CounterListNode : ILiveWatchNode
    {
        public string UniqueID => "counters$";
        public string RawType => "Counter List";
        public string Name => "Counters";
        public LiveWatchCapabilities Capabilities { get; } = LiveWatchCapabilities.CanHaveChildren;
        public LiveWatchPhysicalLocation Location => default;

        ILiveWatchEngine _Engine;
        public CounterListNode(ILiveWatchEngine engine)
        {
            _Engine = engine;
        }

        public ILiveWatchNode[] GetChildren(LiveWatchChildrenRequestReason reason)
        {
            var result = new List<ILiveWatchNode>();
            var counter = _Engine.Symbols.LookupVariable("g_Counter");
            if (counter != null)
                result.Add(new CounterNode(_Engine, counter));

            return result.ToArray();
        }

        public void Dispose()
        {
        }

        public void SetSuspendState(LiveWatchNodeSuspendState state)
        {
        }

        public void SetValueAsString(string newValue) => new NotImplementedException();

        public LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            return new LiveWatchNodeState { Value = "Changed" };
        }
    }
}