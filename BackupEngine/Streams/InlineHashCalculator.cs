using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BackupEngine.Util.Streams
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
}
