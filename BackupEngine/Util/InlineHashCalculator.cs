using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BackupEngine.Util
{
    public class InlineHashCalculator
    {
        private Stream _inputStream,
                       _outputStream;
        private HashAlgorithm _hashAlgorithm;
        private const int DefaultSize = 1 << 12;
        private byte[] _inputBuffer,
                       _hashBuffer,
                       _outputBuffer;
        private AutoResetEvent _hashReadyEvent,
                               _writerReadyEvent,
                               _done;

        public InlineHashCalculator(Stream input, HashAlgorithm hash, Stream output)
        {
            _inputStream = input;
            _hashAlgorithm = hash;
            _outputStream = output;
        }

        public void DoCopy()
        {
            _hashReadyEvent = new AutoResetEvent(true);
            _writerReadyEvent = new AutoResetEvent(true);
            _done = new AutoResetEvent(false);
            ReadMore();
            _done.WaitOne();
        }

        private void ReadMore()
        {
            _inputBuffer = new byte[DefaultSize];
            var task = _inputStream.ReadAsync(_inputBuffer, 0, _inputBuffer.Length);
            task.ContinueWith(FinishedRead);
        }

        private void FinishedRead(Task<int> task)
        {
            _hashReadyEvent.WaitOne();
            _writerReadyEvent.WaitOne();
            if (task.Result == 0)
            {
                _done.Set();
                return;
            }
            _hashBuffer = _inputBuffer;
            ReadMore();
            _outputBuffer = _hashBuffer;
            WriteMore(task.Result);
            _hashAlgorithm.TransformBlock(_hashBuffer, 0, task.Result, null, 0);
            _hashReadyEvent.Set();
        }

        private void WriteMore(int count)
        {
            var task = _outputStream.WriteAsync(_outputBuffer, 0, count);
            task.ContinueWith(FinishedWrite);
        }

        private void FinishedWrite(Task task)
        {
            _writerReadyEvent.Set();
        }
    }

    public abstract class HashCalculatorFilter : Stream
    {
        protected Stream Stream;
        protected HashAlgorithm Hash;
        protected readonly bool KeepOpen;

        protected HashCalculatorFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true)
        {
            Stream = stream;
            Hash = hash;
            KeepOpen = keepOpen;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (!KeepOpen && Stream != null)
            {
                Stream.Dispose();
                Stream = null;
            }
        }
    }

    public class HashCalculatorInputFilter : HashCalculatorFilter
    {

        public HashCalculatorInputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true) : base(stream, hash, keepOpen) { }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ret = Stream.Read(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, ret, null, 0);
            return ret;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { return Stream.Length; }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }

    public class HashCalulatorOutputFilter : HashCalculatorFilter
    {
        public HashCalulatorOutputFilter(Stream stream, HashAlgorithm hash, bool keepOpen = true) : base(stream, hash, keepOpen) { }

        public override void Flush()
        {
            Stream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException();
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var task = Stream.WriteAsync(buffer, offset, count);
            Hash.TransformBlock(buffer, offset, count, null, 0);
            task.Wait();
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new InvalidOperationException(); }
        }

        public override long Position
        {
            get { throw new InvalidOperationException(); }
            set { throw new InvalidOperationException(); }
        }
    }
}
