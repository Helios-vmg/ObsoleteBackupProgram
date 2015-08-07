using System;
using System.IO;
using System.Security.Cryptography;
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
                var inputFile = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var report = new InputProgressReport(100);
                var sha2 = SHA256.Create();
                var calculator = new HashCalculatorInputFilter(inputFile, sha2, false);
                using (var filter = new ProgressInputFilter(calculator, report.ProgressCallback, inputFile.Length, false))
                {
                    var outputFile = new FileStream(outputPath1, FileMode.Create, FileAccess.Write, FileShare.None);
                    using (var lzma = new LzmaOutputFilter(outputFile, false))
                        filter.CopyTo(lzma);
                }
                sha2.TransformFinalBlock(new byte[0], 0, 0);
                Console.WriteLine("\n" + BitConverter.ToString(sha2.Hash).Replace("-", "").ToLower());
            }
            Console.WriteLine("Decompressing...");
            {
                var inputFile = new FileStream(outputPath1, FileMode.Open, FileAccess.Read, FileShare.Read);
                var report = new OutputProgressReport(100);
                var sha2 = SHA256.Create();
                var progressInputFilter = new ProgressInputFilter(inputFile, report.InputProgressCallback,
                    inputFile.Length, false);
                using (var lzma = new LzmaInputFilter(progressInputFilter, false))
                {
                    var outputFile = new FileStream(outputPath2, FileMode.Create, FileAccess.Write, FileShare.None);
                    var calculator = new HashCalculatorOutputFilter(outputFile, sha2, false);
                    using (var progressOutputFilter = new ProgressOutputFilter(calculator, report.OutputProgressCallback, 0, false))
                        lzma.CopyTo(progressOutputFilter);
                }
                sha2.TransformFinalBlock(new byte[0], 0, 0);
                Console.WriteLine("\n" + BitConverter.ToString(sha2.Hash).Replace("-", "").ToLower());
            }
        }
    }
}
