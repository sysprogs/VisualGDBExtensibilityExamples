using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisualGDBExpressions;

namespace MyTypeVisualizer
{
    class MyBasicFilter : TypeListBasedExpressionFilter
    {
        public MyBasicFilter()
            : base("VeryBasicArray<")
        {
        }

        protected override IExpression DoAttach(IExpression expr, IExpressionEvaluator evaluator)
        {
            string countExpression = string.Format("({0}).m_Count", expr.FullNameForEvaluator);
            var value = evaluator.EvaluateExpression(countExpression, "int") as ExpressionValue.Integral;
            if (value == null)
                return null;

            ulong count = value.Value;
            var actualMembers = new VirtualExpressionNode("[Actual members]", "", expr.Children);

            var result = new StaticExpressionFilter(expr);
            result.ValueOverride = new ExpressionValue.Custom(
                        string.Format("An array with {0} items",
                        count));

            string format = "(" + expr.FullNameForEvaluator + ").m_pData[{0}]";

            result.ChildrenOverride = new ArrayChildProvider(evaluator,
                format, 0, (int)count,
                actualMembers);

            return result;
        }
    }
}
