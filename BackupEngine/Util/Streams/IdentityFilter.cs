using System.IO;

namespace BackupEngine.Util.Streams
{
    public class IdentityInputFilter : InputFilter
    {
        public IdentityInputFilter(Stream stream, bool keepOpen = true):base(stream, keepOpen)
        {
        }
    }

    public class IdentityOutputFilter : OutputFilter
    {
        public IdentityOutputFilter(Stream stream, bool keepOpen = true)
            : base(stream, keepOpen)
        {
        }
    }
}