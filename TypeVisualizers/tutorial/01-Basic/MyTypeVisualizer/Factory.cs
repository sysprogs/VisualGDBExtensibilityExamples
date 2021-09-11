using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisualGDBExpressions;

namespace MyTypeVisualizer
{
    public class MyFilterFactory : IExpressionFilterFactory
    {
        public IEnumerable<ExpressionFilterRecord> CreateExpressionFilters()
        {
            return new ExpressionFilterRecord[] { new MyBasicFilter().Record };
        }
    }
}
