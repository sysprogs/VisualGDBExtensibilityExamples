using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VisualGDBExtensibility.LiveWatch;

namespace StackAndHeapLiveWatch
{
    class IARHeapStructureNode : NodeBase
    {
        IPinnedVariable _HeapInfoVariable;

        public struct HeapInfoField
        {
            public string Name, Description;

            public HeapInfoField(string name, string description)
            {
                Name = name;
                Description = description;
            }
        }

        List<ILiveWatchNode> _Variables = new List<ILiveWatchNode>();
        int _MaxHeapSize;

        ILiveVariable _AllocatedSizeVariable;

        public IARHeapStructureNode(ILiveWatchEngine engine)
            : base("$heap")
        {
            _HeapInfoVariable = engine.Symbols.LookupVariable("IARHeapInfo");
            Name = "Heap";
            Capabilities = LiveWatchCapabilities.CanHaveChildren | LiveWatchCapabilities.DoNotHighlightChangedValue;

            var heapBase = engine.Symbols.TryLookupRawSymbolInfo("HEAP$$Base");
            var heapLimit = engine.Symbols.TryLookupRawSymbolInfo("HEAP$$Limit");
            if (heapBase.HasValue && heapLimit.HasValue)
                _MaxHeapSize = (int)(heapLimit.Value.Address - heapBase.Value.Address);

            if (_HeapInfoVariable != null)
            {
                var fields = new[]
                {
                    new HeapInfoField("uordblks", "Allocated Space"),
                    new HeapInfoField("fordblks", "Free Space"),
                    new HeapInfoField("arena", "Committed Space"),
                    new HeapInfoField("ordblks", "Number of Free Chunks"),
                };

                foreach(var field in fields)
                {
                    var fld = _HeapInfoVariable.LookupSpecificChild(field.Name);
                    if (fld == null)
                        continue;

                    if (field.Name == "uordblks")
                        _AllocatedSizeVariable = engine.CreateLiveVariable(fld);

                    var lv = engine.CreateNodeForPinnedVariable(fld, new LiveWatchNodeOverrides { Name = field.Description });
                    if (lv != null)
                        _Variables.Add(lv);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public override void SetSuspendState(LiveWatchNodeSuspendState state)
        {
            base.SetSuspendState(state);
        }

        public override ILiveWatchNode[] GetChildren(LiveWatchChildrenRequestReason reason)
        {
            return _Variables.ToArray();
        }

        public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            if (_HeapInfoVariable == null)
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = "No 'IARHeapInfo' variable found. Please add it to the project and update it periodically." };

            if (_AllocatedSizeVariable == null)
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = "'IARHeapInfo' variable does not have the expected structure." };

            var allocated = _AllocatedSizeVariable.GetValue().ToUlong();
            if (_MaxHeapSize == 0)
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Heap, Value = $"{allocated} bytes allocated" };
            else
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Heap, Value = $"{allocated}/{_MaxHeapSize} bytes allocated" };

        }
    }
}
