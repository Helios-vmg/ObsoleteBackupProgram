using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public abstract class HashCalculatorFilter : Filter
    {
        protected long BytesProcessed = 0;
        protected HashAlgorithm Hash;

        protected HashCalculatorFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true): base(stream, keepOpen)
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
    }
}