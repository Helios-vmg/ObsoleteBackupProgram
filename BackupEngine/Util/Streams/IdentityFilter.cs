using System;
using System.IO;

namespace BackupEngine.Util.Streams
{
    public class IdentityFilter : Filter
    {
        private long _bytesProcessed = 0;

        public IdentityFilter(Stream stream, bool keepOpen = true):base(stream, keepOpen)
        {
        }

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
            var ret = Stream.Read(buffer, offset, count);
            _bytesProcessed += ret;
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream.Write(buffer, offset, count);
            _bytesProcessed += count;
        }

        public override bool CanRead
        {
            get { return Stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return Stream.CanWrite; }
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

        public override long BytesIn
        {
            get { return _bytesProcessed; }
        }

        public override long BytesOut
        {
            get { return _bytesProcessed; }
        }
    }
}