using System.IO;

namespace BackupEngine.Util.Streams
{
    public abstract class Filter : EncapsulatableInputStream
    {
        protected Stream Stream;
        protected bool KeepOpen;
        public abstract long BytesIn { get; }
        public abstract long BytesOut { get; }

        protected Filter(Stream stream, bool keepOpen = true)
        {
            Stream = stream;
            KeepOpen = keepOpen;
        }
    }
}
