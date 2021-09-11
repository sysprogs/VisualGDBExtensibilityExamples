using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    class VectorFilter : TypeListBasedExpressionFilter
    {
        public VectorFilter()
            : base("std::vector<", "stlpmtx_std::vector<", "std::__debug::vector<")
        {
        }

        protected override IExpression DoAttach(IExpression expr, IExpressionEvaluator evaluator)
        {
            string fullName = expr.FullNameForEvaluator;
            var tmp = evaluator.EvaluateExpression(string.Format("({0})._M_impl._M_finish-({0})._M_impl._M_start", fullName), null);
            string impl = "._M_impl";
            if (tmp == null)
            {
                tmp = evaluator.EvaluateExpression(string.Format("({0})._M_finish-({0})._M_start", fullName), null);
                impl = "";
            }
            if (tmp is ExpressionValue.Integral)
            {
                var len = (tmp as ExpressionValue.Integral).Value;

                var filteredExpr = new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom(string.Format("[{0} items]", len)) };

                var actualMembersNode = new VirtualExpressionNode("[actual members]", "", expr.Children) { FixedValue = new ExpressionValue.Composite("{...}") };
                //filteredExpr.ChildrenOverride = new StaticChildProvider(actualMembersNode);
                filteredExpr.ChildrenOverride = new ArrayChildProvider(evaluator, string.Format("({0}){2}._M_start[{1}]", fullName, "{0}", impl), 0, (int)len, actualMembersNode);
                return filteredExpr;
            }

            return null;
        }
    }
}
