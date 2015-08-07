using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BackupEngine.Util.Streams
{
    internal class LzmaInputStream : NativeInputStream
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

    public class LzmaInputFilter : InputFilter
    {
        private Stream _filteredStream;

        private static Stream Filter(Stream stream)
        {
            return new LzmaInputStream(new EncapsulatedInputStream(stream));
        }

        public LzmaInputFilter(Stream stream, bool keepOpen = true)
            : base(Filter(stream), keepOpen)
        {
            _filteredStream = stream;
        }

        protected override void InternalDispose()
        {
            if (_filteredStream == null)
                return;
            _filteredStream.Dispose();
            _filteredStream = null;
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            BytesRead += ret;
            return ret;
        }
    }

    public class LzmaOutputFilter : OutputFilter
    {
        private Stream _filteredStream;

        private static Stream Filter(Stream stream)
        {
            return new LzmaOutputStream(new EncapsulatedOutputStream(stream));
        }

        public LzmaOutputFilter(Stream stream, bool keepOpen = true)
            : base(Filter(stream), keepOpen)
        {
            _filteredStream = stream;
        }

        protected override void InternalDispose()
        {
            if (_filteredStream == null)
                return;
            _filteredStream.Dispose();
            _filteredStream = null;
        }
    }
}
