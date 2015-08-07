using System.IO;
using BackupEngine.Util.Streams;

namespace BackupEngine.Archive
{
    public abstract class FilterGenerator
    {
        public abstract InputFilter FilterInput(Stream stream, bool leaveOpen);
        public abstract OutputFilter FilterOutput(Stream stream, bool leaveOpen);
        public abstract bool IsCompression { get; }
        public abstract bool IsEncryption { get; }
    }
}
