using System;
using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public class HashCalculatorInputFilter : InputFilter
    {
        protected long BytesProcessed = 0;
        protected HashAlgorithm Hash;

        public HashCalculatorInputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true)
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

        public override long BytesIn
        {
            get { return BytesProcessed; }
        }

        public override long BytesOut
        {
            get { return BytesProcessed; }
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, ret, null, 0);
            BytesProcessed += ret;
            return ret;
        }
    }
}