using System.IO;

namespace BackupEngine.Util.Streams
{
    public abstract class InputFilter : EncapsulatableInputStream
    {
        protected Stream Stream;
        protected bool KeepOpen;
        public long BytesIn { get; protected set; }

        protected InputFilter(Stream stream, bool keepOpen = true)
        {
            Stream = stream;
            KeepOpen = keepOpen;
        }

        protected virtual void InternalDispose() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !KeepOpen)
            {
                InternalDispose();
                if (Stream != null)
                {
                    Stream.Dispose();
                    Stream = null;
                }
            }
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            BytesIn += ret;
            return ret;
        }
    }

    public abstract class OutputFilter : EncapsulatableOutputStream
    {
        protected Stream Stream;
        protected bool KeepOpen;
        public long BytesOut { get; protected set; }

        protected OutputFilter(Stream stream, bool keepOpen = true)
        {
            Stream = stream;
            KeepOpen = keepOpen;
        }

        protected virtual void InternalDispose() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !KeepOpen)
            {
                if (Stream != null)
                {
                    Stream.Dispose();
                    Stream = null;
                }
                InternalDispose();
            }
        }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Stream.Write(buffer, offset, count);
        }
    }
}
