using System;
using System.IO;
using System.Runtime.InteropServices;
using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    internal class CounterNode : ILiveWatchNode
    {
        private ILiveWatchEngine _Engine;
        private IPinnedVariable _Counter;

        public CounterNode(ILiveWatchEngine engine, IPinnedVariable counter)
        {
            _Engine = engine;
            _Counter = counter;
        }

        public string UniqueID => "counter:" + _Counter.UserFriendlyName;
        public string RawType => "Sample Counter";
        public string Name => _Counter.UserFriendlyName + " from " + Path.GetFileName(_Counter.SourceLocation.File ?? "");

        public LiveWatchCapabilities Capabilities => LiveWatchCapabilities.CanHaveChildren;

        public LiveWatchPhysicalLocation Location
        {
            get
            {
                return new LiveWatchPhysicalLocation(_Counter.Address,
                    _Counter.SourceLocation.File,
                    _Counter.SourceLocation.Line);
            }
        }

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

        public void SetValueAsString(string newValue)
        {
            throw new NotSupportedException();
        }

        public LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            return default;
        }
    }
}