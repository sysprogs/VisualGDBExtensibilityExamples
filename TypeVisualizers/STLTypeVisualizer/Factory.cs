using System;
using System.Collections.Generic;
using System.Text;
using VisualGDBExpressions;

namespace STLTypeVisualizer
{
    public class FilterFactory : IExpressionFilterFactory
    {
        public IEnumerable<ExpressionFilterRecord> CreateExpressionFilters()
        {
            List<ExpressionFilterRecord> filters = new List<ExpressionFilterRecord>();

            filters.Add(new StringFilter().Record);
            filters.Add(new VectorFilter().Record);
            filters.Add(new ListFilter().Record);
            filters.Add(new SetFilter().Record);
            filters.Add(new MapFilter().Record);
            filters.Add(new SharedPtrFilter().Record);

            return filters;
        }
    }
}
