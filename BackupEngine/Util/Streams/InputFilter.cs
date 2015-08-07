using System.IO;

namespace BackupEngine.Util.Streams
{
    public abstract class InputFilter : EncapsulatableInputStream
    {
        protected Stream Stream;
        protected bool KeepOpen;
        public abstract long BytesIn { get; }
        public abstract long BytesOut { get; }

        protected InputFilter(Stream stream, bool keepOpen = true)
        {
            Stream = stream;
            KeepOpen = keepOpen;
        }
    }

    public abstract class OutputFilter : EncapsulatableOutputStream
    {
        protected Stream Stream;
        protected bool KeepOpen;
        public abstract long BytesIn { get; }
        public abstract long BytesOut { get; }

        protected OutputFilter(Stream stream, bool keepOpen = true)
        {
            Stream = stream;
            KeepOpen = keepOpen;
        }
    }
}
