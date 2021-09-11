using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    class SharedPtrFilter : TypeListBasedExpressionFilter
    {
        public SharedPtrFilter()
            : base("std::unique_ptr<")
        {
        }

        protected override IExpression DoAttach(IExpression expr, IExpressionEvaluator evaluator)
        {
            string fullName = expr.FullNameForEvaluator;
            var target = evaluator.CreateExpression(string.Format("({0})._M_t._M_head_impl", fullName), true);
            if (target == null)
                return null;

            var val = target.Value as ExpressionValue.Integral;
            if (val?.Value == 0)
            {
                return new StaticExpressionFilter(expr) { ValueOverride = new ExpressionValue.Custom("empty") };
            }
            

            return target;
        }
    }
}
