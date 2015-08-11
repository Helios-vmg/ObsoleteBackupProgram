using System;
using System.IO;

namespace BackupEngine.Util.Streams
{
    class BoundedStream : Stream
    {
        private Stream _stream;
        private long _size;
        private long _bytesRead;

        public BoundedStream(Stream stream, long boundedSize)
        {
            _stream = stream;
            _size = boundedSize;
            _bytesRead = 0;
        }

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
            if (count > _size - _bytesRead)
                count = (int)(_size - _bytesRead);
            var ret = _stream.Read(buffer, offset, count);
            _bytesRead += ret;
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
            get { return _size; }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }
}
