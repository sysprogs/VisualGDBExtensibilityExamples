using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VisualGDBExtensibility.LiveWatch;

namespace StackAndHeapLiveWatch
{
    class HeapStructureNode : NodeBase
    {
        private readonly ILiveWatchEngine _Engine;
        private readonly ILiveVariable _HeapEndVariable, _FreeListVariable;

        ILiveVariable _LiveHeap;

        const int ChunkSizeOffset = 0, NextChunkPointerOffset = 4;
        const int HeapBlockHeaderSize = 8;

        class HeapMetricNode : ScalarNodeBase
        {
            private Func<ParsedHeapState, int> _Callback;
            private HeapStructureNode _HeapNode;

            public HeapMetricNode(HeapStructureNode heapNode, string idSuffix, string name, Func<ParsedHeapState, int> callback)
                : base(heapNode.UniqueID + idSuffix)
            {
                Name = name;
                _Callback = callback;
                _HeapNode = heapNode;
                SelectedFormatter = _HeapNode._Engine.CreateDefaultFormatter(ScalarVariableType.SInt32);
            }

            public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
            {
                int value = _Callback(_HeapNode._ParsedHeapContents);
                RawValue = new LiveVariableValue(_HeapNode._HeapContents.Timestamp, _HeapNode._HeapContents.Generation, BitConverter.GetBytes(value));
                return new LiveWatchNodeState { Value = SelectedFormatter.FormatValue(RawValue.Value) };
            }
        }

        class HeapBlockNode : NodeBase
        {
            private HeapStructureNode _HeapNode;
            private int _Index;

            public HeapBlockNode(HeapStructureNode heapNode, int index)
                : base($"{heapNode.UniqueID}[{index}]")
            {
                _HeapNode = heapNode;
                _Index = index;
            }

            public override LiveWatchPhysicalLocation Location
            {
                get
                {
                    var blocks = _HeapNode._ParsedHeapContents.Blocks;
                    if (blocks == null || _Index >= blocks.Length)
                        return default;
                    return new LiveWatchPhysicalLocation { Address = _HeapNode._HeapStart + (uint)blocks[_Index].Offset };
                }
                protected set { }
            }

            public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
            {
                var blocks = _HeapNode._ParsedHeapContents.Blocks;
                if (blocks == null || _Index >= blocks.Length)
                    return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = "???" };

                var blk = blocks[_Index];

                return new LiveWatchNodeState
                {
                    Icon = blk.IsAllocated ? LiveWatchNodeIcon.Block : LiveWatchNodeIcon.EmptyBlock,
                    NewName = $"[0x{_HeapNode._HeapStart + (uint)blk.Offset:x8}]",
                    NewType = $"{blk.Size} bytes",
                    Value = blk.IsAllocated ? _HeapNode.FormatBlockContents(blk) : "unallocated"
                };
            }
        }

        private string FormatBlockContents(HeapBlockInfo blk)
        {
            var data = _HeapContents.Value;
            if (data == null)
                return "???";

            StringBuilder result = new StringBuilder();
            for (int i = blk.Offset; i < (blk.Offset + blk.Size) && i < data.Length; i++)
            {
                result.AppendFormat("{0:x2} ", data[i]);
                if (result.Length > 16)
                {
                    result.Append("...");
                    break;
                }
            }

            return result.ToString();
        }

        class HeapBlockListNode : NodeBase
        {
            private HeapStructureNode _HeapNode;

            public HeapBlockListNode(HeapStructureNode heapNode)
                : base(heapNode.UniqueID + ".blocks")
            {
                Name = "[Heap Blocks]";
                _HeapNode = heapNode;
                Capabilities = LiveWatchCapabilities.CanHaveChildren;
            }

            List<HeapBlockNode> _CreatedNodes = new List<HeapBlockNode>();

            public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
            {
                var blocks = _HeapNode._ParsedHeapContents.Blocks;
                if (blocks == null || blocks.Length == 0)
                    return new LiveWatchNodeState { Value = $"[empty]" };
                else
                {
                    List<HeapBlockListNode> nodes = new List<HeapBlockListNode>();
                    var result = new LiveWatchNodeState { Value = $"[{blocks.Length} blocks]" };
                    if (context.PreloadChildren)
                    {
                        while (_CreatedNodes.Count < blocks.Length)
                            _CreatedNodes.Add(new HeapBlockNode(_HeapNode, _CreatedNodes.Count));

                        result.NewChildren = _CreatedNodes.Take(blocks.Length).ToArray();
                    }

                    return result;
                }
            }
        }

        private LiveVariableValue _HeapContents;
        private ParsedHeapState _ParsedHeapContents;
        private ILiveWatchNode[] _Children;

        ulong _HeapStart, _FixedHeapEnd;

        public HeapStructureNode(ILiveWatchEngine engine)
            : base("$heap")
        {
            _Engine = engine;
            Name = "Heap";
            Capabilities = LiveWatchCapabilities.CanHaveChildren | LiveWatchCapabilities.DoNotHighlightChangedValue;

            var fixedSizeHeap = engine.Symbols.TryLookupRawSymbolInfo("FixedSizeHeap");
            if (fixedSizeHeap.HasValue)
            {
                _HeapStart = fixedSizeHeap.Value.Address;
                _FixedHeapEnd = fixedSizeHeap.Value.Address + (uint)fixedSizeHeap.Value.Size;
            }
            else
            {
                _HeapStart = engine.Symbols.TryLookupRawSymbolInfo("end")?.Address ?? 0;

                var heapEndVariableAddress = engine.Symbols.FindSymbolsContainingString("heap_end").SingleOrDefault().Address;
                if (heapEndVariableAddress != 0)
                    _HeapEndVariable = engine.Memory.CreateLiveVariable(heapEndVariableAddress, 4);
            }

            var freeListVariable = engine.Symbols.TryLookupRawSymbolInfo("__malloc_free_list");   //May not have debug symbols, so we whould use the raw symbol API
            if (freeListVariable.HasValue)
                _FreeListVariable = engine.Memory.CreateLiveVariable(freeListVariable.Value.Address, freeListVariable.Value.Size);
        }

        public override void SetSuspendState(LiveWatchNodeSuspendState state)
        {
            base.SetSuspendState(state);
            if (_HeapEndVariable != null)
                _HeapEndVariable.SuspendUpdating = state.SuspendRegularUpdates;
            if (_FreeListVariable != null)
                _FreeListVariable.SuspendUpdating = state.SuspendRegularUpdates;
            if (_LiveHeap != null)
                _LiveHeap.SuspendUpdating = state.SuspendRegularUpdates;
        }

        struct HeapBlockInfo
        {
            public readonly int Offset;
            public readonly int Size;
            public readonly uint Next;
            public bool IsAllocated;

            public HeapBlockInfo(int offset, int size, uint next)
            {
                Offset = offset;
                Size = size;
                Next = next;
                IsAllocated = true; //This will be cleared when parsing the free block list
            }

            public override string ToString()
            {
                return (IsAllocated ? "Allocated" : "Free") + $" block with offset={Offset}, size={Size}";
            }
        }

        struct ParsedHeapState
        {
            public HeapBlockInfo[] Blocks;
            public string Error;
            public int TotalAreaSize;
            public int TotalFreeBlocks, TotalUsedBlocks;
            public int TotalFreeSize, TotalUsedSize;
            public int MaxFreeBlock, MaxUsedBlock;
        }

        public override void Dispose()
        {
            base.Dispose();
            _HeapEndVariable?.Dispose();
        }

        ParsedHeapState ParseHeapContents(byte[] contents, uint freeListHead)
        {
            List<HeapBlockInfo> blocks = new List<HeapBlockInfo>();
            if (contents == null)
                return new ParsedHeapState { Error = "Cannot read heap contents" };

            ParsedHeapState result = new ParsedHeapState { TotalAreaSize = contents.Length };

            ulong heapAddress = _HeapStart;
            int offset = 0;
            Dictionary<int, int> blockNumbersByOffset = new Dictionary<int, int>();

            while (offset <= (contents.Length - HeapBlockHeaderSize))
            {
                if (offset < 0)
                    offset = 0;

                uint pNext = BitConverter.ToUInt32(contents, offset + NextChunkPointerOffset);
                int size = BitConverter.ToInt32(contents, offset + ChunkSizeOffset);

                int increment = size;
                if (increment <= 0)
                    break;

                var block = new HeapBlockInfo(offset + HeapBlockHeaderSize, increment - HeapBlockHeaderSize, pNext);
                blockNumbersByOffset[offset] = blocks.Count;
                blocks.Add(block);
                offset += increment;
            }

            result.Blocks = blocks.ToArray();

            if (offset != contents.Length)
                result.Error = $"Unexpected last block address (0x{_HeapStart + (uint)offset} instead of 0x{_HeapStart + (uint)contents.Length})";
            else
            {
                for (uint freeBlock = freeListHead; freeBlock != 0; freeBlock = GetNextFreeBlock(contents, freeBlock))
                {
                    if (blockNumbersByOffset.TryGetValue((int)(freeBlock - _HeapStart), out int index))
                    {
                        result.Blocks[index].IsAllocated = false;
                    }
                    else
                    {
                        result.Error = $"Free block list references a non-existing block 0x{freeBlock:x8}";
                        break;
                    }

                }
            }

            foreach(var block in result.Blocks)
            {
                if (block.IsAllocated)
                {
                    result.TotalUsedBlocks++;
                    result.TotalUsedSize += block.Size;
                    result.MaxUsedBlock = Math.Max(result.MaxUsedBlock, block.Size);
                }
                else
                {
                    result.TotalFreeBlocks++;
                    result.TotalFreeSize += block.Size;
                    result.MaxFreeBlock = Math.Max(result.MaxUsedBlock, block.Size);
                }
            }

            return result;
        }

        private uint GetNextFreeBlock(byte[] contents, ulong pBlock)
        {
            ulong pNext = pBlock + NextChunkPointerOffset;
            ulong limit = _HeapStart + (uint)contents.Length;
            if ((pNext + 4) > limit)
                return 0;
            return BitConverter.ToUInt32(contents, (int)(pNext - _HeapStart));
        }

        public override LiveWatchNodeState UpdateState(LiveWatchUpdateContext context)
        {
            if (_HeapStart == 0 || _FreeListVariable == null)
                return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Error, Value = "Heap analysis only works with newlib-nano" };

            var result = new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Heap };

            if (context.PreloadChildren)
            {
                ulong heapEnd = _FixedHeapEnd;
                if (_HeapEndVariable != null)
                {
                    heapEnd = _HeapEndVariable.GetValue().ToUlong();
                    if (heapEnd == 0)
                    {
                        return new LiveWatchNodeState { Icon = LiveWatchNodeIcon.Heap, Value = "The heap has not been used yet" };
                    }
                }

                if (_LiveHeap?.Address != _HeapStart || _LiveHeap?.Size != (int)(heapEnd - _HeapStart))
                    _LiveHeap = _Engine.Memory.CreateLiveVariable(_HeapStart, (int)(heapEnd - _HeapStart));

                _HeapContents = _LiveHeap.GetValue();
                _ParsedHeapContents = ParseHeapContents(_HeapContents.Value, (uint)_FreeListVariable.GetValue().ToUlong());

                if (_Children == null)
                {
                    _Children = new ILiveWatchNode[]
                    {
                        _HeapEndVariable == null ? null : new HeapMetricNode(this, ".dynamic.size", "Dynamic Heap Area Size", st => st.TotalAreaSize),
                        new HeapMetricNode(this, ".used.size", "Allocated Bytes", st => st.TotalUsedSize),
                        new HeapMetricNode(this, ".used.count", "Allocated Blocks", st => st.TotalUsedBlocks),
                        new HeapMetricNode(this, ".used.max", "Max. Allocation Size", st => st.MaxUsedBlock),
                        new HeapMetricNode(this, ".used.size", "Immediately available bytes", st => st.TotalFreeSize),
                        new HeapMetricNode(this, ".used.count", "Free Blocks", st => st.TotalFreeBlocks),
                        new HeapMetricNode(this, ".used.max", "Max. Free Block Size", st => st.MaxFreeBlock),
                        new HeapBlockListNode(this),
                    }.Where(x => x != null).ToArray();
                }

                result.NewChildren = _Children;
                result.Value = $"{_ParsedHeapContents.TotalUsedSize} bytes allocated";
            }
            else
                result.Value = "(expand heap node to see details)";

            return result;
        }
    }

}
