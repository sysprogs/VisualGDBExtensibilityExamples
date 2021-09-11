using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    class ListFilter : TypeListBasedExpressionFilter
    {
        public ListFilter()
            : base("std::list<", "std::__debug::list<")
        {
        }

        class ListNode
        {
            public UInt64 NextNodeAddr;
            public IExpression Value;
        }

        class ListChildProvider : IExpressionChildProvider
        {
            IExpressionEvaluator _Evaluator;
            string _ListType;
            ulong _InitialNodeAddr;
            IExpression _ActualMembersNode;

            bool _UseStorageSyntax;

            public ListChildProvider(IExpressionEvaluator evaluator, string listType, ulong initialNodeAddr, IExpression actualMembersNode)
            {
                _Evaluator = evaluator;
                _ListType = listType;
                _InitialNodeAddr = initialNodeAddr;
                _ActualMembersNode = actualMembersNode;
            }

            ListNode QueryNode(UInt64 addr, bool skipValue)
            {
                var nextAddr = _Evaluator.EvaluateExpression(string.Format("(void *)(({0}::_Node *)0x{1:x})->_M_next", _ListType, addr), "void *");
                if (!(nextAddr is ExpressionValue.Integral))
                    return null;

                ListNode node = new ListNode { NextNodeAddr = (nextAddr as ExpressionValue.Integral).Value };
                if (!skipValue)
                {
                    if (!_UseStorageSyntax)
                        node.Value = _Evaluator.CreateExpression(string.Format("(({0}::_Node *)0x{1:x})->_M_data", _ListType, addr), true);

                    if (node.Value == null)
                    {
                        node.Value = _Evaluator.CreateExpression(string.Format("*(({0}::value_type *)&((({0}::_Node *)0x{1:x})->_M_storage))", _ListType, addr), true);
                        if (node.Value != null)
                            _UseStorageSyntax = true;
                    }

                    if (node.Value == null)
                        return null;
                }

                return node;
            }

            List<IExpression> _Children;

            void LoadChildren()
            {
                if (_Children != null)
                    return;

                _Children = new List<IExpression>{_ActualMembersNode};

                int idx = 0;

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
                }
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
            fullType = fullType.TrimEnd('&');

            var dummyNodeAddrObj = evaluator.EvaluateExpression(string.Format("(void *)&(({0})._M_impl._M_node)", fullName), "void *");
            if (dummyNodeAddrObj is ExpressionValue.Integral)
            {
                ulong dummyNodeAddr = (dummyNodeAddrObj as ExpressionValue.Integral).Value;

                var filteredExpr = new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom("[expand to populate std::list]") };
                var actualMembersNode = new VirtualExpressionNode("[actual members]", "", expr.Children) { FixedValue = new ExpressionValue.Composite("{...}") };

                filteredExpr.ChildrenOverride = new ListChildProvider(evaluator, fullType, dummyNodeAddr, actualMembersNode);
                return filteredExpr;
            }

            return null;
        }
    }
}
