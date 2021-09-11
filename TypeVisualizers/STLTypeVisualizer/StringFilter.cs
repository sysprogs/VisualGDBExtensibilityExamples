using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    class StringFilter : TypeListBasedExpressionFilter
    {
        public StringFilter()
            : base("std::string", "std::basic_string<", "std::wstring")
        {
        }

        protected override IExpression DoAttach(IExpression expr, IExpressionEvaluator evaluator)
        {
            string fullName = expr.FullNameForEvaluator;
            var tmp = evaluator.EvaluateExpression(string.Format("({0})._M_dataplus._M_p", fullName), null);
            if (tmp == null)
                return null;

            string strVal = tmp.ToString();
            if (strVal.StartsWith("0x"))
            {
                int idx = strVal.IndexOf(' ');
                if (idx != -1)
                    strVal = strVal.Substring(idx + 1);
            }

            var filteredExpr = new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom(strVal) };

            if (strVal.StartsWith("\"") && strVal.EndsWith("\""))
                filteredExpr.RawStringValueOverride = StaticExpressionFilter.RemoveQuotesAndUnescape(strVal.Trim());

            tmp = evaluator.EvaluateExpression(string.Format("((unsigned *)({0})._M_dataplus._M_p)[-3]", fullName), null);
            if (tmp is ExpressionValue.Integral)
            {
                var len = (tmp as ExpressionValue.Integral).Value;

                var actualMembersNode = new VirtualExpressionNode("[actual members]", "", expr.Children) { FixedValue = new ExpressionValue.Composite("{...}") };
                //filteredExpr.ChildrenOverride = new StaticChildProvider(actualMembersNode);
                filteredExpr.ChildrenOverride = new ArrayChildProvider(evaluator, string.Format("({0})._M_dataplus._M_p[{1}]", fullName, "{0}"), 0, (int)len, actualMembersNode);
            }

            return filteredExpr;
        }
    }
}
