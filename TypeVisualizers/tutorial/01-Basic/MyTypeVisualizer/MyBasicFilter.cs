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
            var type = expr.Type;

            return new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom("Hello, Visual Studio") };
        }
    }
}
