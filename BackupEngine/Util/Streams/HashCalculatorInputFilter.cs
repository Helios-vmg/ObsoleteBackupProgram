using System;
using System.IO;
using System.Security.Cryptography;

namespace BackupEngine.Util.Streams
{
    public class HashCalculatorInputFilter : HashCalculatorFilter
    {
        public HashCalculatorInputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true) : base(stream, hash, keepOpen) { }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, ret, null, 0);
            BytesProcessed += ret;
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { return Stream.Length; }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }
}