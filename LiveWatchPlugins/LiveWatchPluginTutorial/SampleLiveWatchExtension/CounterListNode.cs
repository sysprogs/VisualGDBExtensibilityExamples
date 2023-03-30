using System;
using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class CounterListNode : ILiveWatchNode
    {
        public string UniqueID => "counters$";
        public string RawType => "Counter List";
        public string Name => "Counter";
        public LiveWatchCapabilities Capabilities { get; } = LiveWatchCapabilities.CanHaveChildren;
        public LiveWatchPhysicalLocation Location => default;

        public void Dispose()
        {
        }

        public ILiveWatchNode[] GetChildren(LiveWatchChildrenRequestReason reason)
        {
            return null;
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