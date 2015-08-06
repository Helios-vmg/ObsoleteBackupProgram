using System;
using System.Runtime.InteropServices;

namespace BackupEngine.Util.Streams
{
    public class NativeInputStream : EncapsulatableInputStream
    {
        private IntPtr _stream;

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static int read_from_input_stream(IntPtr stream, byte[] buffer, int offset, int length);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public extern static void release_input_stream(IntPtr stream);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ReadCallback(IntPtr buffer, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    private delegate bool EofCallback();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    private delegate void ReleaseCallback();

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static IntPtr encapsulate_dot_net_input_stream(ReadCallback read, EofCallback eof, ReleaseCallback release);

        private static ReadCallback GenerateReadCallback(EncapsulatableInputStream stream)
        {
            return delegate(IntPtr dst, int size)
            {
                var temp = new byte[Math.Min(4096, size)];
                int ret = 0;
                while (size > 0 && !stream.Eof)
                {
                    var read = stream.Read(temp, 0, Math.Min(size, temp.Length));
                    Marshal.Copy(temp, 0, dst + ret, read);
                    size -= read;
                    ret += read;
                }
                return ret;
            };
        }

        private class Helper
        {
            public GCHandle Handle1;
            public GCHandle Handle2;
            public GCHandle Handle3;

            public ReleaseCallback Rc;

            public void Release()
            {
                Handle1.Free();
                Handle2.Free();
                Handle3.Free();
                Rc();
            }
        }

        public static IntPtr EncapsulateDotNetInputStream(EncapsulatableInputStream stream)
        {
            var c1 = GenerateReadCallback(stream);
            var gc1 = GCHandle.Alloc(c1);
            EofCallback c2 = () => stream.Eof;
            var gc2 = GCHandle.Alloc(c2);
            var helper = new Helper
            {
                Handle1 = gc1,
                Handle2 = gc2,
                Rc = stream.Dispose,
            };
            ReleaseCallback c3 = helper.Release;
            helper.Handle3 = GCHandle.Alloc(c3);
            return encapsulate_dot_net_input_stream(c1, c2, c3);
        }

        public NativeInputStream(IntPtr stream)
        {
            _stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            if (_stream != IntPtr.Zero)
            {
                release_input_stream(_stream);
                _stream = IntPtr.Zero;
            }
        }

        ~NativeInputStream()
        {
            Dispose(false);
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            return read_from_input_stream(_stream, buffer, offset, count);
        }
    }

    public class NativeOutputStream : EncapsulatableOutputStream
    {
        private IntPtr _stream;

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static void write_to_output_stream(IntPtr stream, byte[] buffer, int offset, int length);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static void flush_output_stream(IntPtr stream);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public extern static void release_output_stream(IntPtr stream);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WriteCallback(IntPtr buffer, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    private delegate void FlushCallback();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	    private delegate void ReleaseCallback();

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private extern static IntPtr encapsulate_dot_net_output_stream(WriteCallback w, FlushCallback f, ReleaseCallback r);

        private static WriteCallback GenerateWriteCallback(EncapsulatableOutputStream stream)
        {
            return delegate(IntPtr buffer, int size)
            {
                var temp = new byte[Math.Min(4096, size)];
                var offset = 0;
                while (size > 0)
                {
                    var written = Math.Min(size, temp.Length);
                    Marshal.Copy(buffer + offset, temp, 0, written);
                    stream.Write(temp, 0, written);
                    size -= written;
                    offset += written;
                }
            };
        }

        private class Helper
        {
            public GCHandle Handle1;
            public GCHandle Handle2;
            public GCHandle Handle3;

            public ReleaseCallback Rc;

            public void Release()
            {
                Handle1.Free();
                Handle2.Free();
                Handle3.Free();
                Rc();
            }
        }

        public static IntPtr EncapsulateDotNetOutputStream(EncapsulatableOutputStream stream)
        {
            var c1 = GenerateWriteCallback(stream);
            var gc1 = GCHandle.Alloc(c1);
            FlushCallback c2 = stream.Flush;
            var gc2 = GCHandle.Alloc(c2);
            var helper = new Helper
            {
                Handle1 = gc1,
                Handle2 = gc2,
                Rc = stream.Dispose,
            };
            ReleaseCallback c3 = helper.Release;
            helper.Handle3 = GCHandle.Alloc(c3);
            return encapsulate_dot_net_output_stream(c1, c2, c3);
        }

        public NativeOutputStream(IntPtr stream)
        {
            _stream = stream;
        }

        protected override void Dispose(bool disposing)
        {
            if (_stream != IntPtr.Zero)
            {
                release_output_stream(_stream);
                _stream = IntPtr.Zero;
            }
        }

        ~NativeOutputStream()
        {
            Dispose(false);
        }

        public override void Flush()
        {
            flush_output_stream(_stream);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            write_to_output_stream(_stream, buffer, offset, count);
        }
    }
}
