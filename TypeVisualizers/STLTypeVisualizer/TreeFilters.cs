using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    abstract class TreeFilter : TypeListBasedExpressionFilter
    {
        protected TreeFilter(params string[] supportedTypes)
            : base(supportedTypes)
        {
        }

        protected abstract IExpression CreateNodeExpression(IExpressionEvaluator evaluator, string treeNodeExpr, ref int idx);
        protected abstract string ExpandLabel { get; }

        class TreeNode
        {
            public string SelfExpr;
            public UInt64 LeftAddr, RightAddr;
        }

        class TreeChildProvider : IExpressionChildProvider
        {
            IExpressionEvaluator _Evaluator;
            string _ListType;
            ulong _RootNodeAddr;
            IExpression _ActualMembersNode;

            TreeFilter _Filter;

            public TreeChildProvider(IExpressionEvaluator evaluator, string listType, ulong rootNodeAddr, IExpression actualMembersNode, TreeFilter filter)
            {
                _Evaluator = evaluator;
                _ListType = listType;
                _RootNodeAddr = rootNodeAddr;
                _ActualMembersNode = actualMembersNode;
                _Filter = filter;
            }

            TreeNode QueryNode(UInt64 addr)
            {
                var addrObj = _Evaluator.EvaluateExpression(string.Format("(void *)(({0}::_Rep_type::_Link_type)0x{1:x})->_M_left", _ListType, addr), "void *");
                if (!(addrObj is ExpressionValue.Integral))
                    return null;

                TreeNode node = new TreeNode { LeftAddr = (addrObj as ExpressionValue.Integral).Value };
                addrObj = _Evaluator.EvaluateExpression(string.Format("(void *)(({0}::_Rep_type::_Link_type)0x{1:x})->_M_right", _ListType, addr), "void *");
                if (!(addrObj is ExpressionValue.Integral))
                    return null;

                node.RightAddr = (addrObj as ExpressionValue.Integral).Value;

                node.SelfExpr = string.Format("(({0}::_Rep_type::_Link_type)0x{1:x})", _ListType, addr);
                return node;
            }

            List<IExpression> _Children;

            void DoAddNodeRecursively(TreeNode node, ref int idx)
            {
                if (node == null)
                    return;

                if (node.LeftAddr != 0)
                    DoAddNodeRecursively(QueryNode(node.LeftAddr), ref idx);

                _Children.Add(_Filter.CreateNodeExpression(_Evaluator, node.SelfExpr, ref idx));

                if (node.RightAddr != 0)
                    DoAddNodeRecursively(QueryNode(node.RightAddr), ref idx);
            }

            void LoadChildren()
            {
                if (_Children != null)
                    return;

                _Children = new List<IExpression>{_ActualMembersNode};

                var node = QueryNode(_RootNodeAddr);
                if (node == null)
                    return;

                int idx = 0;
                DoAddNodeRecursively(node, ref idx);


                /*

                DateTime start = DateTime.Now;

                for (ListNode node = QueryNode(_InitialNodeAddr, true); node != null; node = QueryNode(node.NextNodeAddr, false))
                {
                    if (node.Value != null)
                        _Children.Add(new StaticExpressionFilter(node.Value) { ShortNameOverride = string.Format("[{0}]", idx++) });

                    if (node.NextNodeAddr == _InitialNodeAddr)
                        break;
                    if ((DateTime.Now - start).TotalSeconds > 10)
                    {
                        _Children.Add(new VirtualExpressionNode("[list traversal timed out]", ""));
                        break;
                    }
                }*/
            }

            public bool ChildrenAvailable
            {
                get { return true; }
            }

            public int QueryChildrenCount(out ExpressionChildRange additionallyReturnedChildren)
            {
                LoadChildren();
                additionallyReturnedChildren = null;
                if (_Children != null)
                    return _Children.Count;
                return 0;
            }

            public ExpressionChildRange QueryChildren(int firstChild, int childCount)
            {
                return new ExpressionChildRange { FirstChildIndex = 0, Children = _Children };
            }
        }


        protected override IExpression DoAttach(IExpression expr, IExpressionEvaluator evaluator)
        {
            string fullName = expr.FullNameForEvaluator;
            string fullType = expr.Type;
            //HACK! This is used by the logic that renames known typedefs to the original type names, but using the pre-typedef name
            //confuses GDB. This may have unforeseen side effects, but we are leaving it this way due to low probability of problems.
            if ((expr as StaticExpressionFilter)?.TypeOverride != null)
                fullType = (expr as StaticExpressionFilter).BaseExpression.Type;

            fullType = fullType.TrimEnd('&');

            var rootNodeAddrObj = evaluator.EvaluateExpression(string.Format("(void *)(({0})._M_t._M_impl._M_header._M_parent)", fullName), "void *");
            if (rootNodeAddrObj is ExpressionValue.Integral)
            {
                var elCountObj = evaluator.EvaluateExpression(string.Format("(void *)(({0})._M_t._M_impl._M_node_count)", fullName), "int");
                string label;
                if (elCountObj is ExpressionValue.Integral)
                    label = string.Format("[{0} items]", (elCountObj as ExpressionValue.Integral).Value);
                else
                    label = ExpandLabel;

                ulong rootNodeAddr = (rootNodeAddrObj as ExpressionValue.Integral).Value;

                var filteredExpr = new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom(label) };
                var actualMembersNode = new VirtualExpressionNode("[actual members]", "", expr.Children) { FixedValue = new ExpressionValue.Composite("{...}") };

                filteredExpr.ChildrenOverride = new TreeChildProvider(evaluator, fullType, rootNodeAddr, actualMembersNode, this);
                return filteredExpr;
            }

            return null;
        }
    }

    class SetFilter : TreeFilter
    {
        public SetFilter()
            : base("std::set<", "std::__debug::set<")
        {
        }

        protected override IExpression CreateNodeExpression(IExpressionEvaluator evaluator, string treeNodeExpr, ref int idx)
        {
            var exp = evaluator.CreateExpression(treeNodeExpr + "->_M_value_field", true);
            if (exp == null)
                exp = evaluator.CreateExpression("*((__typeof__(" + treeNodeExpr + "->_M_valptr()))&(" + treeNodeExpr + "->_M_storage->_M_storage))", true);
            if (exp == null)
                return null;

            return new StaticExpressionFilter(exp) { ShortNameOverride = string.Format("[{0}]", idx++) };
        }

        protected override string ExpandLabel { get { return "[expand to populate std::set]"; } }
    }

    class MapFilter : TreeFilter
    {
        public MapFilter()
            : base("std::map<", "std::__debug::map<")
        {
        }

        protected override IExpression CreateNodeExpression(IExpressionEvaluator evaluator, string treeNodeExpr, ref int idx)
        {
            string prefix = treeNodeExpr + "->_M_value_field.";
            var exp = evaluator.CreateExpression(prefix + "first", true);
            if (exp == null)
            {
                prefix = "((__typeof__(" + treeNodeExpr + "->_M_valptr()))&(" + treeNodeExpr + "->_M_storage->_M_storage))->";
                exp = evaluator.CreateExpression(prefix + "first", true);
            }
            if (exp == null)
                return null;

            string key = exp.Value.ToString();

            exp = evaluator.CreateExpression(prefix + "second", true);
            if (exp == null)
                return null;
            var val = exp.Value;

            exp = evaluator.CreateExpression(prefix.TrimEnd('.', '-', '>'), true);
            if (exp == null)
                return null;

            return new StaticExpressionFilter(exp) { ShortNameOverride = key, ValueOverride = val };
        }

        protected override string ExpandLabel { get { return "[expand to populate std::map]"; } }
    }
}
