using System.IO;
using BackupEngine.Util.Streams;

namespace BackupEngine.Archive
{
    public abstract class FilterGenerator
    {
        public abstract InputFilter Filter(Stream stream, bool leaveOpen);
        public abstract bool IsCompression { get; }
        public abstract bool IsEncryption { get; }
    }
}
