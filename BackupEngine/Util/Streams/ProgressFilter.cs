using System;
using System.IO;
using System.Text;

namespace BackupEngine.Util.Streams
{
    public class ProgressInputFilter : IdentityInputFilter
    {
        public delegate void ProgressCallback(long progress, long total);

        private ProgressCallback _callback;
        private long _progress;
        private long _total;

        public ProgressInputFilter(Stream stream, ProgressCallback callback, long total, bool keepOpen = true) : base(stream, keepOpen)
        {
            _callback = callback;
            _total = total;
        }

        protected override int InternalRead(byte[] buffer, int offset, int count)
        {
            var ret = base.InternalRead(buffer, offset, count);
            _progress += ret;
            _callback(_progress, _total);
            return ret;
        }
    }

    public class ProgressOutputFilter : IdentityOutputFilter
    {
        private ProgressInputFilter.ProgressCallback _callback;
        private long _progress;
        private long _total;

        public ProgressOutputFilter(Stream stream, ProgressInputFilter.ProgressCallback callback, long total, bool keepOpen = true)
            : base(stream, keepOpen)
        {
            _callback = callback;
            _total = total;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            _progress += count;
            _callback(_progress, _total);
        }
    }

    public class InputProgressReport
    {
        private readonly int _width;
        private DateTime? _start;

        public InputProgressReport(int width)
        {
            _width = width;
        }

        public void ProgressCallback(long progress, long total)
        {
            if (_start == null)
                _start = DateTime.Now;
            var secs = (DateTime.Now - _start.Value).TotalMilliseconds / 1000.0;
            long speed = 0;
            if (secs > 0)
                speed = (long)(progress / secs);
            var filled = (int)(progress * (_width - 2) / total);
            WriteProgressBar(filled, _width, speed);
        }

        public static void WriteProgressBar(int filled, int full, long speed)
        {
            var sb = new StringBuilder();
            sb.Append("\r[");
            while (sb.Length < filled + 1)
                sb.Append('#');
            while (sb.Length < (full - 1))
                sb.Append(' ');
            sb.Append("]  ");
            if (speed > 0)
            {
                sb.Append(BaseBackupEngine.FormatSize(speed));
                sb.Append("/s");
            }
            sb.Append("         ");
            Console.Write(sb.ToString());
        }
    }

    public class OutputProgressReport
    {
        private readonly int _width;
        private DateTime? _start;
        private int _currentPosition;

        public OutputProgressReport(int width)
        {
            _width = width;
        }

        public void InputProgressCallback(long progress, long total)
        {
            _currentPosition = (int)(progress * (_width - 2) / total);
        }

        public void OutputProgressCallback(long progress, long total)
        {
            if (_start == null)
                _start = DateTime.Now;
            var secs = (DateTime.Now - _start.Value).TotalMilliseconds / 1000.0;
            long speed = 0;
            if (secs > 0)
                speed = (long)(progress / secs);
            InputProgressReport.WriteProgressBar(_currentPosition, _width, speed);
        }
    }
}
