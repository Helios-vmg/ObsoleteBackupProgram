using System;
using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public class HashCalulatorOutputFilter : HashCalculatorFilter
    {
        public HashCalulatorOutputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true) : base(stream, hash, keepOpen) { }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var task = Stream.WriteAsync(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, count, null, 0);
            BytesProcessed += count;
            task.Wait();
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new InvalidOperationException(); }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }
}