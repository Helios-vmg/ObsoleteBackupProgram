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
                    var outputFile = new FileStream(outputPath1, FileMode.Create, FileAccess.Write, FileShare.None);
                    using (var encapOutput = new EncapsulatedOutputStream(outputFile))
                    using (var lzma = new LzmaOutputStream(encapOutput))
                        inputFile.CopyTo(lzma);
                }
            }
            Console.WriteLine("Decompressing...");
            {
                var inputFile = new FileStream(outputPath1, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var encapInput = new EncapsulatedInputStream(inputFile))
                using (var lzma = new LzmaInputStream(encapInput))
                {
                    using (var outputFile = new FileStream(outputPath2, FileMode.Create, FileAccess.Write, FileShare.None))
                        lzma.CopyTo(outputFile);
                }
            }
        }
    }
}
