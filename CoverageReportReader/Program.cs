using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoverageReportReader
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CoverageReportReader <file.scovreport>");
                Environment.ExitCode = 1;
                return;
            }

            using (var reader = new VisualGDB.Backend.LinuxProfiler.CoverageReportReader(args[0], null))
            {
                foreach(var entry in reader.AllFunctions)
                {
                    /*  public class FunctionEntry
                        {
                            public string Name { get; }
                            public string DeclaringFile { get; }
                            public int DeclaringLine { get; }
                            public ulong CallCount { get; }
                            public CoverageSummary LineCoverage { get; }
                            public CoverageSummary BlockCoverage { get; }
                            public string SourceLocation { get; }
                        }  
                    */
                    
                    Console.WriteLine($"{entry.Name}: {entry.CallCount} calls, {entry.LineCoverage} lines, {entry.BlockCoverage} blocks");
                }
            }
        }
    }
}
