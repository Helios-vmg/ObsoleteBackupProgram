using System;
using System.IO;
using BackupEngine.Util.Streams;

namespace managed_streams_test
{
    class Program
    {
        static void Main()
        {
            const string inputPath = "test.bin";
            const string outputPath1 = "test.bin.xz";
            const string outputPath2 = "test2.bin";
            

            Console.WriteLine("Compressing...");
            {
                using (var inputFile = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var report = new InputProgressReport(100);
                    using (var filter = new ProgressInputFilter(inputFile, report.ProgressCallback, inputFile.Length))
                    {
                        var outputFile = new FileStream(outputPath1, FileMode.Create, FileAccess.Write, FileShare.None);
                        using (var encapOutput = new EncapsulatedOutputStream(outputFile))
                        using (var lzma = new LzmaOutputStream(encapOutput))
                            filter.CopyTo(lzma);
                    }
                }
            }
            Console.WriteLine("\nDecompressing...");
            {
                var inputFile = new FileStream(outputPath1, FileMode.Open, FileAccess.Read, FileShare.Read);
                var report = new OutputProgressReport(100);
                using (var progressInputFilter = new ProgressInputFilter(inputFile, report.InputProgressCallback, inputFile.Length))
                using (var encapInput = new EncapsulatedInputStream(progressInputFilter))
                using (var lzma = new LzmaInputStream(encapInput))
                {
                    using (var outputFile = new FileStream(outputPath2, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var progressOutputFilter = new ProgressOutputFilter(outputFile, report.OutputProgressCallback, 0))
                        lzma.CopyTo(progressOutputFilter);
                }
            }
        }
    }
}
