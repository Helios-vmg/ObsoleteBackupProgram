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

    public class CompressionFilterGenerator : FilterGenerator
    {
        public override InputFilter FilterInput(Stream stream, bool leaveOpen)
        {
            return new LzmaInputFilter(stream, leaveOpen);
        }

        public override OutputFilter FilterOutput(Stream stream, bool leaveOpen)
        {
            return new LzmaOutputFilter(stream, leaveOpen);
        }

        public override bool IsCompression
        {
            get { return true; }
        }

        public override bool IsEncryption
        {
            get { return false; }
        }
    }
}
