using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine.Util.Streams
{
    public class LzmaInputStream : NativeInputStream
    {
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static IntPtr filter_input_stream_through_lzma(IntPtr stream);
        

        private static IntPtr Filter(EncapsulatableInputStream stream)
        {
            var encapsulated = EncapsulateDotNetInputStream(stream);
            try
            {
                return filter_input_stream_through_lzma(encapsulated);
            }
            finally
            {
                release_input_stream(encapsulated);
            }
        }

        public LzmaInputStream(EncapsulatableInputStream stream)
            : base(Filter(stream))
        {
        }
    }

    public class LzmaOutputStream : NativeOutputStream
    {
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static IntPtr filter_output_stream_through_lzma(IntPtr stream);

        private static IntPtr Filter(EncapsulatableOutputStream stream)
        {
            var encapsulated = EncapsulateDotNetOutputStream(stream);
            try
            {
                return filter_output_stream_through_lzma(encapsulated);
            }
            finally
            {
                release_output_stream(encapsulated);
            }
        }

        public LzmaOutputStream(EncapsulatableOutputStream stream)
            : base(Filter(stream))
        {
        }
    }
}
