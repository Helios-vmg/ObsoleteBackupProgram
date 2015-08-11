using System;
using System.IO;

namespace BackupEngine.Util.Streams
{
    public abstract class EncapsulatableInputStream : Stream
    {
        protected abstract int InternalRead(byte[] buffer, int offset, int count);

        public sealed override int Read(byte[] buffer, int offset, int count)
        {
            var ret = InternalRead(buffer, offset, count);
            if (ret <= 0)
                Eof = true;
            return ret;
        }

        public bool Eof { get; private set; }

        public override void Flush()
        {
            throw new InvalidOperationException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
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
            get { throw new InvalidOperationException(); }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }

    public abstract class EncapsulatableOutputStream : Stream
    {
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
            throw new InvalidOperationException();
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

    public class EncapsulatedInputStream : EncapsulatableInputStream
    {
        private readonly Stream _stream;

        public EncapsulatedInputStream(Stream stream)
        {
            _stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _stream.Dispose();
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }
    }

    public class EncapsulatedOutputStream : EncapsulatableOutputStream
    {
        private readonly Stream _stream;

        public EncapsulatedOutputStream(Stream stream)
        {
            _stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _stream.Dispose();
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }
    }
}
