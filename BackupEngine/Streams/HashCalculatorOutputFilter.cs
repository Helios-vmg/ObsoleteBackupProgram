using System;
using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public class HashCalculatorOutputFilter : OutputFilter
    {
        protected HashAlgorithm Hash;

        public HashCalculatorOutputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true)
            : base(stream, keepOpen)
        {
            Hash = hash;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (!KeepOpen && Stream != null)
            {
                Stream.Dispose();
                Stream = null;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, count, null, 0);
        }
    }
}