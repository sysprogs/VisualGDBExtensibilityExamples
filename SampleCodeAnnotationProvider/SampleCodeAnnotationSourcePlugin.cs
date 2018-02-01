using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VisualGDB.Backend.Annotations.Public;

namespace SampleCodeAnnotationProvider
{
    public class SampleCodeAnnotationSourcePlugin : ICodeAnnotationPlugin
    {
        class AnnotationSource : IEmbeddedCodeAnnotationSource
        {
            private ICodeAnnotationProject _Project;

            public AnnotationSource(ICodeAnnotationProject prj)
            {
                _Project = prj;
            }

            public void Dispose()
            {
            }

            public CodeAnnotationSet GetAllAnnotationsForSourceFile(string filePath)
            {
                //The logic below is for demonstration only and won't catch non-trivial cases like functions in header files.
                var suFile = Path.Combine(_Project.ObjectFileDirectory, Path.ChangeExtension(Path.GetFileName(filePath), ".su"));
                if (!File.Exists(suFile))
                    return default(CodeAnnotationSet);

                List<CodeAnnotationRecord> results = new List<CodeAnnotationRecord>();
                Regex rgStackUsageLine = new Regex("([^:]+):([0-9]+):[0-9]*:([^\t]*)\t([0-9]+)\t.*");
                Regex rgFunctionName = new Regex(@"[^\(\)]+ ([a-zA-Z0-9_]+) *\(");

                foreach (var line in File.ReadAllLines(suFile))
                {
                    var m = rgStackUsageLine.Match(line);
                    if (m.Success)
                    {
                        int depth = int.Parse(m.Groups[4].Value);
                        string functionName = m.Groups[3].Value;
                        string file = m.Groups[1].Value;
                        int lineNum = int.Parse(m.Groups[2].Value);

                        if (StringComparer.InvariantCultureIgnoreCase.Compare(file, Path.GetFileName(filePath)) != 0)
                            continue;

                        var matchingSym = _Project.SymbolDependencies.AllSymbols.FirstOrDefault(sym => sym.Name == functionName);
                        if (matchingSym == null)
                        {
                            var m2 = rgFunctionName.Match(functionName);
                            if (m2.Success)
                            {
                                string nameOnly = m2.Groups[1].Value;
                                matchingSym = _Project.SymbolDependencies.AllSymbols.FirstOrDefault(sym => sym.Name == nameOnly);
                            }
                        }

                        if (matchingSym != null)
                        {
                            //This demonstrates how to look up symbols using their addresses
                            var sym2 = _Project.SymbolDependencies.LookupSymbolByAddress(matchingSym.Address);
                            if (sym2 != matchingSym)
                                throw new Exception("Symbol lookup is not working");
                        }

                        results.Add(new CodeAnnotationRecord
                        {
                            Location = new SysprogsDevTools.SourceCoordinates(lineNum - 1, 0),
                            Annotation = new  SampleCodeAnnotation(functionName, depth, matchingSym),
                        });
                    }
                }

                return new CodeAnnotationSet { Records = results.ToArray() };
            }
        }

        public IEmbeddedCodeAnnotationSource AttachToProject(ICodeAnnotationProject prj)
        {
            return new AnnotationSource(prj);
        }
    }
}
