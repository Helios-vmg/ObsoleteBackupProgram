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

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = base.InternalRead(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, ret, null, 0);
            return ret;
        }
    }
}