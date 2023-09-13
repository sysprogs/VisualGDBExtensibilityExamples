using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class SampleLiveWatchNodeSource : ILiveWatchNodeSource
    {
        ILiveWatchNode[] _Nodes;

        public SampleLiveWatchNodeSource(ILiveWatchEngine engine)
        {
            _Nodes = new ILiveWatchNode[]
            {
                new CounterListNode(engine)
            };
        }

        public void Dispose()
        {
        }

        public ILiveWatchNode[] PerformPeriodicUpdatesFromBackgroundThread() => _Nodes;
    }
}