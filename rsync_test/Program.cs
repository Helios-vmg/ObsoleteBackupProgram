using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ionic.Zip;
using Ionic.Zlib;

namespace rsync_test
{
    public abstract class StreamBlockReader : IDisposable
    {
        public int BlockSize { get; private set; }

        protected Stream InputStream;

        protected byte[] NextBuffer;

        protected IAsyncResult LastRead;

        public bool EndOfFile { get; protected set; }

        public enum OperationMode
        {
            BlockByBlock,
            MovingWindow,
        }
        public OperationMode Mode { get; private set; }

        public void Dispose()
        {
            InputStream.Dispose();
        }

        protected StreamBlockReader(Stream stream, int blockSize)
        {
            InputStream = stream;
            BlockSize = blockSize;
            NextBuffer = new byte[BlockSize];
            ReadMore();
        }

        protected void ReadMore()
        {
            if (!EndOfFile)
                LastRead = InputStream.BeginRead(NextBuffer, 0, NextBuffer.Length, AsyncCallback, 0);
        }

        protected abstract void AsyncCallback(IAsyncResult ar);
    }

    public class BlockByBlockStreamReader : StreamBlockReader
    {
        private byte[] _currentBuffer;

        public BlockByBlockStreamReader(Stream stream, int blockSize) : base(stream, blockSize)
        {
        }

        protected override void AsyncCallback(IAsyncResult ar)
        {
        }

        public byte[] NextBlock()
        {
            if (EndOfFile)
                return null;
            var bytesRead = InputStream.EndRead(LastRead);
            _currentBuffer = NextBuffer;
            NextBuffer = new byte[BlockSize];
            if (bytesRead == 0)
            {
                EndOfFile = true;
                return null;
            }
            if (bytesRead < BlockSize)
            {
                EndOfFile = true;
                var temp = new byte[bytesRead];
                Array.Copy(_currentBuffer, temp, temp.Length);
                _currentBuffer = null;
                return temp;
            }
            var ret = _currentBuffer;
            _currentBuffer = null;
            ReadMore();
            return ret;
        }
    }

    public class ByteByByteStreamReader : BlockByBlockStreamReader
    {
        private byte[] _currentBlock;
        private int _offset;

        public ByteByByteStreamReader(Stream stream, int blockSize) : base(stream, blockSize)
        {
            _currentBlock = NextBlock();
        }

        public bool NextByte(out byte dst)
        {
            if (_currentBlock == null)
            {
                dst = 0;
                return false;
            }
            dst = _currentBlock[_offset++];
            if (_offset == _currentBlock.Length)
            {
                _offset = 0;
                _currentBlock = NextBlock();
            }
            return true;
        }

        public byte[] WholeBlock()
        {
            if (_currentBlock == null)
                return null;
            var temp = NextBlock();
            var size = _currentBlock.Length - _offset;
            if (temp != null)
                size = Math.Min(size + temp.Length, BlockSize);
            byte[] ret;
            if (_offset == 0)
                ret = _currentBlock;
            else
            {
                ret = new byte[size];
                Array.Copy(_currentBlock, _offset, ret, 0, _currentBlock.Length - _offset);
                if (_currentBlock.Length - _offset < size)
                {
                    Debug.Assert(temp != null);
                    Array.Copy(temp, 0, ret, _currentBlock.Length - _offset, size - (_currentBlock.Length - _offset));
                }
            }
            _currentBlock = temp;
            if (_currentBlock != null && _offset >= _currentBlock.Length)
                _currentBlock = null;
            return ret;
        }
    }

    public static class EasySorter
    {
        public static bool LessThan(this byte[] a, byte[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
            {
                var A = a[i];
                var B = b[i];
                if (A > B)
                    return false;
                if (A < B)
                    return true;
            }
            return a.Length < b.Length;
        }

        public static bool GreaterThan(this byte[] a, byte[] b)
        {
            return b.LessThan(a);
        }

        public static Tuple<List<uint>, List<byte[]>, List<long>> Sort(this Tuple<List<uint>, List<byte[]>, List<long>> s)
        {
            var list = new List<int>(s.Item1.Count);
            var index = 0;
            list.AddRange(s.Item1.Select(i => index++));
            list.Sort((x, y) =>
            {
                if (s.Item1[x] < s.Item1[y])
                    return -1;
                if (s.Item1[x] > s.Item1[y])
                    return 1;
                if (s.Item2[x].LessThan(s.Item2[y]))
                    return -1;
                if (s.Item2[x].GreaterThan(s.Item2[y]))
                    return 1;
                if (s.Item3[x] < s.Item3[y])
                    return -1;
                if (s.Item3[x] > s.Item3[y])
                    return 1;
                return 0;
            });
            var ret = new Tuple<List<uint>, List<byte[]>, List<long>>(new List<uint>(), new List<byte[]>(), new List<long>());
            for (var i = 0; i < s.Item1.Count; i++)
            {
                ret.Item1.Add(s.Item1[list[i]]);
                ret.Item2.Add(s.Item2[list[i]]);
                ret.Item3.Add(s.Item3[list[i]]);
            }
            return ret;
        }
    }

    public static class Rsync
    {
        public const int BlockSize = 512;

        #region Rolling checksum
        public static uint RollingChecksum(byte[] buffer, int offset = -1, int length = -1)
        {
            if (offset < 0)
                offset = 0;
            if (length < 0)
                length = buffer.Length - offset;
            uint a = 0,
                b = 0;
            for (var i = 0; i < length; i++)
            {
                uint k = buffer[i + offset];
                a = (a + k) & 0xFFFF;
                b = (uint)((b + k * (length - i + 1)) & 0xFFFF);
            }
            return a | (b << 16);
        }

        public static uint RollingChecksumCircular(byte[] buffer, int head, int offset, int length = -1)
        {
            if (length < 0)
                length = buffer.Length - offset;
            uint a = 0,
                b = 0;
            for (var i = 0; i < length; i++)
            {
                uint k = buffer[(i + head + offset) % length];
                a = (a + k) & 0xFFFF;
                b = (uint)((b + k * (length - i + 1)) & 0xFFFF);
            }
            return a | (b << 16);
        }

        public static uint RollingChecksumRemove(uint previous, byte x, uint l)
        {
            uint a = previous & 0xFFFF,
                b = ((previous >> 16) & 0xFFFF);
            a = (a + 0x10000 - x) & 0xFFFF;
            b = (b + 0xFFFF0000 - x * (l + 1)) & 0xFFFF;
            return a | (b << 16);
        }

        public static uint RollingChecksumAdd(uint previous, byte x, uint l)
        {
            uint a = previous & 0xFFFF,
                b = ((previous >> 16) & 0xFFFF);
            a = (a + x) & 0xFFFF;
            b = (uint)((b + a + x) & 0xFFFF);
            return a | (b << 16);
        }
        #endregion

        public static Tuple<List<uint>, List<byte[]>, List<long>> DigestOldFile(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            using (var stream = new BlockByBlockStreamReader(file, BlockSize))
            {
                var ret = new Tuple<List<uint>, List<byte[]>, List<long>>(new List<uint>(), new List<byte[]>(), new List<long>());
                var md5 = MD5.Create();
                byte[] block;
                for (long offset = 0; (block = stream.NextBlock()) != null; offset += block.Length)
                {
                    var checksum = RollingChecksum(block);
                    var hash = md5.ComputeHash(block);
                    ret.Item1.Add(checksum);
                    ret.Item2.Add(hash);
                    ret.Item3.Add(offset);
                }
                return ret.Sort();
            }
        }

        public class Command
        {
            public enum Type
            {
                CopyOld,
                CopyNew,
            }

            public Type CommandType;

            public long Offset,
                Length;
        }

        private static long FindOffsetInStructure(Tuple<List<uint>, List<byte[]>, List<long>> s, uint x, byte[] buf = null,
            int begin = -1, int end = -1)
        {
            var index = FindInStructure(s, x, buf, begin, end);
            if (index < 0)
                return -1;
            return s.Item3[index];
        }

        private static int FindInStructure(Tuple<List<uint>, List<byte[]>, List<long>> s, uint x, byte[] buf = null, int begin = -1, int end = -1)
        {
            if (begin < 0)
                begin = 0;
            if (end < 0)
                end = s.Item1.Count;
            while (begin < end)
            {
                var pivot = begin + (end - begin) / 2;
                var val = s.Item1[pivot];
                if (x < val)
                    end = pivot;
                else if (x > val)
                    begin = pivot + 1;
                else if (buf == null)
                    return pivot;
                else
                {
                    var buf2 = s.Item2[pivot];
                    if (buf.LessThan(buf2))
                        end = pivot;
                    else if (buf2.LessThan(buf))
                        begin = pivot + 1;
                    else
                        return pivot;
                }
            }
            return -1;
        }

        private class FileComparer : IDisposable
        {
            private Tuple<List<uint>, List<byte[]>, List<long>> _structure;
            private readonly List<Command> _result = new List<Command>();
            private readonly MD5 _md5 = MD5.Create();
            private long _offset = 0;
            private FileStream _file;
            private ByteByByteStreamReader _stream;
            private byte[] _circularBuffer = null;
            private int _circularBufferHead = 0;
            private int _circularBufferLength = 0;
            private uint _currentChecksum = 0;
            private int _currentState = 0;
            private const int InitialState = 0;
            private const int NotFoundState = 1;
            private const int FoundState = 2;
            private const int FinalState = 3;
            private long _oldOffset = -1;
            private bool _quit = false;

            private Action[] _states;

            public FileComparer(string oldFile, string newFile)
            {
                _structure = DigestOldFile(oldFile);
                _file = new FileStream(newFile, FileMode.Open, FileAccess.Read, FileShare.None);
                _stream = new ByteByByteStreamReader(_file, BlockSize);

                _states = new Action[]
                {
                    ProcessState0,
                    ProcessState1,
                    ProcessState2,
                    ProcessState3,
                };
            }

            public List<Command> Process()
            {
                while (!_quit)
                {
                    if (_currentState < InitialState || _currentState > FinalState)
                        throw new BadStateException();
                    _states[_currentState]();
                }
                return _result;
            }

            private bool DoSearch()
            {
                _oldOffset = -1;
                var index = FindInStructure(_structure, _currentChecksum);
                if (index >= 0)
                {
                    _md5.Initialize();
                    _md5.TransformBlock(_circularBuffer, _circularBufferHead,
                        _circularBuffer.Length - _circularBufferHead, _circularBuffer, _circularBufferHead);
                    _md5.TransformFinalBlock(_circularBuffer, 0, _circularBufferHead);
                    if (_structure.Item2[index] == _md5.Hash)
                        _oldOffset = _structure.Item3[index];
                    else
                        _oldOffset = FindOffsetInStructure(_structure, _currentChecksum, _md5.Hash);
                    if (_oldOffset >= 0)
                        return true;
                }
                return false;
            }

            private void ProcessState0()
            {
                _circularBuffer = _stream.WholeBlock();
                if (_circularBuffer == null)
                {
                    _currentState = FinalState;
                    return;
                }
                _circularBufferHead = 0;
                _circularBufferLength = _circularBuffer.Length;
                _currentChecksum = RollingChecksum(_circularBuffer);
                if (!DoSearch())
                    _currentState = NotFoundState;
                else
                    _currentState = FoundState;
            }

            private void ProcessState1()
            {
                var command = new Command
                {
                    CommandType = Command.Type.CopyNew,
                    Offset = _offset,
                    Length = 0,
                };
                _result.Add(command);

                do
                {
                    _offset++;
                    _result[_result.Count - 1].Length++;

                    _currentChecksum = RollingChecksumRemove(_currentChecksum, _circularBuffer[_circularBufferHead], (uint)_circularBufferLength);
                    var more = _stream.NextByte(out _circularBuffer[_circularBufferHead]);
                    _circularBufferHead = (_circularBufferHead + 1) % _circularBuffer.Length;
                    if (!more)
                    {
                        if (--_circularBufferLength == 0)
                        {
                            _currentState = FinalState;
                            return;
                        }
                    }
                    else
                        _currentChecksum = RollingChecksumAdd(_currentChecksum,
                            _circularBuffer[(_circularBufferHead + _circularBufferLength - 1) % _circularBuffer.Length], (uint)_circularBufferLength);
                } while (!DoSearch());
                _currentState = FoundState;
            }

            private void ProcessState2()
            {
                while (true)
                {
                    var command = new Command
                    {
                        CommandType = Command.Type.CopyOld,
                        Offset = _oldOffset,
                        Length = 0,
                    };
                    _result.Add(command);

                    Command last;
                    while (true)
                    {
                        _offset += BlockSize;
                        last = _result[_result.Count - 1];
                        last.Length += _circularBufferLength;
                        _circularBuffer = _stream.WholeBlock();
                        if (_circularBuffer == null)
                        {
                            _currentState = FinalState;
                            return;
                        }
                        _circularBufferHead = 0;
                        _circularBufferLength = _circularBuffer.Length;
                        _currentChecksum = RollingChecksum(_circularBuffer);
                        if (!DoSearch())
                        {
                            _currentState = NotFoundState;
                            return;
                        }
                        if (_oldOffset != last.Offset + last.Length)
                            break;
                    }
                }
            }

            private void ProcessState3()
            {
                _quit = true;
            }

            public void Dispose()
            {
                _stream.Dispose();
                _file.Dispose();
            }
        }

        public static List<Command> CompareFiles(string oldFile, string newFile)
        {
            try
            {
                using (var comparer = new FileComparer(oldFile, newFile))
                {
                    var ret = comparer.Process();
                    var sum = ret.Sum(x => x.Length);
                    var oldLength = new FileInfo(oldFile).Length;
                    var newLength = new FileInfo(newFile).Length;
                    return ret;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void TransformFile(string oldPath, string newPath, List<Command> commands, string resultPath)
        {
            var buffer = new byte[4096];
            using (var oldFile = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var newFile = new FileStream(newPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var resultFile = new FileStream(resultPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var command in commands)
                {
                    FileStream srcStream;
                    switch (command.CommandType)
                    {
                        case Command.Type.CopyOld:
                            srcStream = oldFile;
                            break;
                        case Command.Type.CopyNew:
                            srcStream = newFile;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    srcStream.Seek(command.Offset, SeekOrigin.Begin);

                    for (var bytesToCopy = command.Length; bytesToCopy > 0; bytesToCopy -= 4096)
                    {
                        var n = (int)Math.Min(bytesToCopy, 4096);
                        srcStream.Read(buffer, 0, n);
                        resultFile.Write(buffer, 0, n);
                    }
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            const string oldPath = @"g:\Backup\000\version0.zip";
            const string newPath = @"g:\Backup\000\version1.zip";
            const string resultPath = @"g:\Backup\000\version2.zip";
            var testCommands = new List<Rsync.Command>
            {
                new Rsync.Command
                {
                    CommandType = Rsync.Command.Type.CopyOld,
                    Offset = 0,
                    Length = 1033,
                },
                new Rsync.Command
                {
                    CommandType = Rsync.Command.Type.CopyOld,
                    Offset = 10330,
                    Length = 3323,
                },
                new Rsync.Command
                {
                    CommandType = Rsync.Command.Type.CopyOld,
                    Offset = 10330 / 2,
                    Length = 4021,
                },
            };
            Rsync.TransformFile(oldPath, oldPath, testCommands, newPath);
            var commands = Rsync.CompareFiles(oldPath, newPath);
            Rsync.TransformFile(oldPath, newPath, commands, resultPath);
        }
    }
}
