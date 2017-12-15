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

                        results.Add(new CodeAnnotationRecord
                        {
                            Location = new SysprogsDevTools.SourceCoordinates(lineNum - 1, 0),
                            Annotation = new  SampleCodeAnnotation(functionName, depth),
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
