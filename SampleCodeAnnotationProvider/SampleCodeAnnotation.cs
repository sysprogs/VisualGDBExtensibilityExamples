using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using VisualGDB.Backend.Annotations.Public;

namespace SampleCodeAnnotationProvider
{
    class SampleCodeAnnotation : ICodeAnnotation
    {
        private string _FunctionName;
        private int _Depth;
        private ISymbolWithReferences _Symbol;

        public SampleCodeAnnotation(string functionName, int depth, ISymbolWithReferences matchingSym)
        {
            _FunctionName = functionName;
            _Depth = depth;
            _Symbol = matchingSym;
        }


        public object TypeKey => typeof(SampleCodeAnnotation);

        public int InTypePriority => 0;

        public int SortOrder => 1000;

        public double InitialHeight => 16;


        public System.Windows.FrameworkElement CreateInstance()
        {
            return new StackUsageAnnotation { DataContext = new StackUsageAnnotation.Data(_Depth, $"{_FunctionName} uses {_Depth} bytes of stack", _Symbol) };
        }
    }
}
