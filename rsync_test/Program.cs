using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

#if false
    public class MovingWindowStreamReader : StreamBlockReader
    {
        private byte[] _currentBuffer1,
            _currentBuffer2;
        private int _bufferOffset;

        public MovingWindowStreamReader(Stream stream, int blockSize)
            : base(stream, blockSize)
        {
        }

        protected override void AsyncCallback(IAsyncResult ar)
        {
            lock (this)
            {
                _currentBuffer1 = _currentBuffer2;
                _currentBuffer2 = NextBuffer;
                NextBuffer = new byte[BlockSize];
                if (_currentBuffer1 == null)
                    ReadMore();
            }
        }

        public bool ReadAndShift(byte[] dst)
        {
            Debug.Assert(dst.Length == BlockSize);
            for (int i = 0; i < 3; i++)
            {
                IAsyncResult lr;
                lock (this)
                {
                    lr = LastRead;
                    if (_currentBuffer1 != null)
                    {
                        Array.Copy(_currentBuffer1, _bufferOffset, dst, 0, BlockSize - _bufferOffset);
                        if (_bufferOffset > 0)
                            Array.Copy(_currentBuffer2, 0, dst, BlockSize - _bufferOffset, _bufferOffset);
                        if (++_bufferOffset == BlockSize)
                        {
                            _currentBuffer1 = null;
                            _bufferOffset = 0;
                            ReadMore();
                        }
                    }
                    if (EndOfFile)
                        return false;
                }
                InputStream.EndRead(lr);
            }
            throw new Exception("The object is in an inconsistent state.");
        }
    }
#endif

    public static class EasySorter
    {
        public static bool LessThan(this byte[] a, byte[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++)
                if (a[i] >= b[i])
                    return false;
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

        private enum ComparisonState
        {
            Undefined,
            Unmatched,
            Matched,
        }

        public static List<Command> CompareFiles(string oldFile, string newFile)
        {
            try
            {
                var structure = DigestOldFile(oldFile);
                var ret = new List<Command>();
                var md5 = MD5.Create();
                long offset = 0;
                using (var file = new FileStream(newFile, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var stream = new ByteByByteStreamReader(file, BlockSize))
                {
                    byte[] circularBuffer = null;
                    int circularBufferHead = 0;
                    int circularBufferLength = 0;
                    uint currentChecksum = 0;
                    var state = ComparisonState.Undefined;
                    var found = false;
                    long oldOffset = -1;
                    while (true)
                    {
                        if (oldOffset >= 0 || state == ComparisonState.Undefined)
                        {
                            var add = false;
                            if (ret.Count > 0)
                            {
                                var last = ret[ret.Count - 1];
                                if (last.CommandType == Command.Type.CopyNew)
                                {
                                    last.Length = offset - last.Offset;
                                    add = true;
                                }
                                else
                                {
                                    last.Length += circularBufferLength;
                                    add = true;
                                }
                            }
                            if (state == ComparisonState.Matched)
                            {
                                if (ret.Count > 0 && ret[ret.Count - 1].CommandType != Command.Type.CopyOld)
                                    ret.Add(new Command
                                    {
                                        CommandType = Command.Type.CopyOld,
                                        Offset = oldOffset,
                                        Length = circularBufferLength,
                                    });
                                add = true;
                            }
                            if (add)
                                offset += circularBufferLength;
                            circularBuffer = stream.WholeBlock();
                            if (circularBuffer == null)
                                return ret;
                            circularBufferHead = 0;
                            circularBufferLength = circularBuffer.Length;
                            currentChecksum = RollingChecksum(circularBuffer);
                        }
                        else
                        {
                            if (ret.Count <= 0 || ret[ret.Count - 1].CommandType != Command.Type.CopyNew)
                                ret.Add(new Command
                                {
                                    CommandType = Command.Type.CopyNew,
                                    Offset = offset,
                                    Length = 0,
                                });

                            currentChecksum = RollingChecksumRemove(currentChecksum, circularBuffer[circularBufferHead], (uint)circularBufferLength);
                            var more = stream.NextByte(out circularBuffer[circularBufferHead]);
                            circularBufferHead = (circularBufferHead + 1) % circularBuffer.Length;
                            offset++;
                            if (!more)
                            {
                                if (--circularBufferLength == 0)
                                {
                                    ret[ret.Count - 1].Length = offset - ret[ret.Count - 1].Offset;
                                    return ret;
                                }
                            }
                            else
                                currentChecksum = RollingChecksumAdd(currentChecksum,
                                    circularBuffer[(circularBufferHead + circularBufferLength - 1) % circularBuffer.Length], (uint)circularBufferLength);
                        }
                        oldOffset = -1;
                        var index = FindInStructure(structure, currentChecksum);
                        if (index >= 0)
                        {
                            md5.Initialize();
                            md5.TransformBlock(circularBuffer, circularBufferHead,
                                circularBuffer.Length - circularBufferHead, circularBuffer, circularBufferHead);
                            md5.TransformFinalBlock(circularBuffer, 0, circularBufferHead);
                            var hash = md5.Hash;
                            oldOffset = FindOffsetInStructure(structure, currentChecksum, hash, index, index + 1);
                            if (oldOffset >= 0)
                                state = ComparisonState.Matched;
                        }
                        else
                            state = ComparisonState.Unmatched;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

    }

    class Program
    {
        static void Main(string[] args)
        {
#if false
            {
                var md5 = MD5.Create();
                var rng = new Random();
                const int length = 1 << 6;
                var buffer = new byte[length];
                rng.NextBytes(buffer);
                md5.Initialize();
                md5.TransformBlock(buffer, 0, length / 2, buffer, 0);
                md5.TransformFinalBlock(buffer, length / 2, length / 2);
                var hash = md5.Hash;
                md5.Initialize();
                md5.TransformBlock(buffer, 0, 0, buffer, 0);
                md5.TransformFinalBlock(buffer, 0, length);
                hash = md5.Hash;
            }
#endif
            var result = Rsync.CompareFiles(@"g:\Backup\000\0", @"g:\Backup\000\1");
#if false
            var rng = new Random();
            const int length = 1 << 6;
            var buffer = new byte[length * 10];
            rng.NextBytes(buffer);
            uint last = 0;
            int lastLength = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var exiting = length > buffer.Length - i;
                var l = !exiting ? length : buffer.Length - i;
                var a = Rsync.RollingChecksum(buffer, i, l);
                uint b;
                if (i == 0)
                    b = a;
                else
                {
                    b = Rsync.RollingChecksumRemove(last, buffer[i - 1], (uint)lastLength);
                    if (!exiting)
                        b = Rsync.RollingChecksumAdd(b, buffer[i + l - 1], (uint)lastLength);
                }
                if (a != b)
                {
                    Console.WriteLine("Problem at offset " + i);
                    return;
                }
                last = b;
                lastLength = l;
            }
#endif
        }
    }
}
