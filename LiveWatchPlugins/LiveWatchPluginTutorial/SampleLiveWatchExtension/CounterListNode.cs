using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        List<CounterNode> _StaticCounters = new List<CounterNode>();

        IPinnedVariableStructType _CounterStruct;
        IPinnedVariableStructMember _NextField;

        public ILiveWatchNode[] GetChildren(LiveWatchChildrenRequestReason reason)
        {
            var result = new List<ILiveWatchNode>();

            foreach (var v in _Engine.Symbols.TopLevelVariables)
            {
                if (v.RawType.Resolved == "SampleCounter")
                    _StaticCounters.Add(new CounterNode(_Engine, v));
            }

            _CounterStruct = _Engine.Symbols.LookupType("SampleCounter") as IPinnedVariableStructType;
            _NextField = _CounterStruct?.LookupMember("Next", false);

            return result.ToArray();
        }

        public void Dispose()
        {
        }

        public void SetSuspendState(LiveWatchNodeSuspendState state)
        {
            lock (_DynamicCounters)
                foreach (var c in _DynamicCounters.Values)
                    if (c.IsValid)
                        c.NextField.SuspendUpdating = state.SuspendRegularUpdates || !state.IsExpanded;
        }

        public void SetValueAsString(string newValue) => new NotImplementedException();

        class WatchedCounter
        {
            public readonly ILiveVariable NextField;
            public readonly CounterNode Node;
            internal int Generation;

            public WatchedCounter(CounterListNode list, ulong addr)
            {
                NextField = list._Engine.Memory.CreateLiveVariable(addr + list._NextField.Offset, list._NextField.Size, $"[{addr:x8}]");

                var tv = list._Engine.Symbols.CreateTypedVariable(addr, list._CounterStruct);
                if (tv != null)
                    Node = new CounterNode(list._Engine, tv);
            }

            public bool IsValid => NextField != null && Node != null;
        }

        Dictionary<ulong, WatchedCounter> _DynamicCounters = new Dictionary<ulong, WatchedCounter>();
        int _Generation;

        public LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            List<ILiveWatchNode> children = new List<ILiveWatchNode>();
            children.AddRange(_StaticCounters);

            _Generation++;
            if (_NextField != null)
            {
                lock (_DynamicCounters)
                {
                    foreach (var head in _StaticCounters)
                    {
                        ulong addr, nextAddr;
                        for (addr = head.Address; addr != 0; addr = nextAddr)
                        {
                            if (!_DynamicCounters.TryGetValue(addr, out WatchedCounter counter))
                                _DynamicCounters[addr] = counter = new WatchedCounter(this, addr);

                            counter.Generation = _Generation;

                            if (!counter.IsValid)
                                break;

                            if (addr != head.Address)
                                children.Add(counter.Node);

                            nextAddr = counter.NextField.GetValue().ToUlong();
                        }
                    }
                }
            }

            lock (_DynamicCounters)
                foreach (var kv in _DynamicCounters.ToArray())
                    if (kv.Value.Generation != _Generation)
                    {
                        kv.Value.NextField?.Dispose();
                        _DynamicCounters.Remove(kv.Key);
                    }

            return new LiveWatchNodeState
            {
                Value = "Changed",
                NewChildren = children.Count == 0 ? null : children.ToArray()
            };
        }
    }
}