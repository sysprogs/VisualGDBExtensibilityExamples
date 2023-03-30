using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class SampleLiveWatchNodeSource : ILiveWatchNodeSource
    {
        ILiveWatchNode[] _Nodes;

        public SampleLiveWatchNodeSource()
        {
            _Nodes = new ILiveWatchNode[]
            {
                new CounterListNode()
            };
        }

        public void Dispose()
        {
        }

        public ILiveWatchNode[] PerformPeriodicUpdatesFromBackgroundThread() => _Nodes;
    }
}