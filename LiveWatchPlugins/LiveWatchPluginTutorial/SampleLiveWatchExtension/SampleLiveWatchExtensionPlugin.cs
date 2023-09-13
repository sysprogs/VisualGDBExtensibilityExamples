using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisualGDBExtensibility.LiveWatch;

namespace SampleLiveWatchExtension
{
    public class SampleLiveWatchExtensionPlugin : ILiveWatchPlugin
    {
        public string UniqueID => "com.sysprogs.example.livewatch.sample";
        public string Name => "Sample Plugin";
        public LiveWatchNodeIcon Icon => LiveWatchNodeIcon.Graph;
        public ILiveWatchNodeSource CreateNodeSource(ILiveWatchEngine engine)
        {
            return new SampleLiveWatchNodeSource(engine);
        }
    }
}
