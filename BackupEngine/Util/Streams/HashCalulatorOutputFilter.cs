using System;
using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public class HashCalulatorOutputFilter : OutputFilter
    {
        protected long BytesProcessed = 0;
        protected HashAlgorithm Hash;

        public HashCalulatorOutputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true)
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

        public override void Flush()
        {
            Stream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var task = Stream.WriteAsync(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, count, null, 0);
            BytesProcessed += count;
            task.Wait();
        }
    }
}