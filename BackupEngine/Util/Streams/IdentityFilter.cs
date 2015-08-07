using System.IO;

namespace BackupEngine.Util.Streams
{
    public class IdentityInputFilter : InputFilter
    {
        private long _bytesProcessed;

        public IdentityInputFilter(Stream stream, bool keepOpen = true):base(stream, keepOpen)
        {
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            _bytesProcessed += ret;
            return ret;
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

    public class IdentityOutputFilter : OutputFilter
    {
        private long _bytesProcessed;

        public IdentityOutputFilter(Stream stream, bool keepOpen = true)
            : base(stream, keepOpen)
        {
        }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream.Write(buffer, offset, count);
            _bytesProcessed += count;
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