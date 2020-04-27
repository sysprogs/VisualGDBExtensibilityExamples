using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualGDBExtensibility.LiveWatch;

namespace StackAndHeapLiveWatch
{
    public class StackAndHeapLiveWatchPlugin : ILiveWatchPlugin
    {
        public string UniqueID => "com.sysprogs.live.stackheap";
        public string Name => "Stack/Heap";
        public LiveWatchNodeIcon Icon => LiveWatchNodeIcon.Heap;

        public ILiveWatchNodeSource CreateNodeSource(ILiveWatchEngine engine) => new NodeSource(engine);

        class NodeSource : ILiveWatchNodeSource
        {
            private ILiveWatchEngine _Engine;

            public NodeSource(ILiveWatchEngine engine)
            {
                _Engine = engine;
            }

            public void Dispose()
            {
            }

            ILiveWatchNode[] _Nodes;

            public ILiveWatchNode[] PerformPeriodicUpdatesFromBackgroundThread()
            {
                if (_Nodes == null)
                {
                    _Nodes = new ILiveWatchNode[] { new StackInfoNode(_Engine), new HeapStructureNode(_Engine) };
                }

                return _Nodes;
            }
        }
    }
}
