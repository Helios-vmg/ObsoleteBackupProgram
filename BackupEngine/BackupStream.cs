using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using ProtoBuf;

namespace BackupEngine
{
    public enum StreamType
    {
        None,
        Full,
        Diff,
    }

    [ProtoContract]
    [ProtoInclude(2001, typeof(FullStream))]
    [ProtoInclude(2002, typeof(DiffStream))]
    public abstract class BackupStream
    {
        [ProtoMember(1)]
        public ulong UniqueId;

        public abstract StreamType Type { get; }
        public FileSystemObject Fso;

        public abstract long VirtualSize { get; }
        public abstract long PhysicalSize { get; }
    }

    [ProtoContract]
    public class FullStream : BackupStream
    {
        public override StreamType Type
        {
            get { return StreamType.Full; }
        }

        [ProtoMember(2)]
        public long _virtualSize;
        [ProtoMember(3)]
        public long _physicalSize;

        public override long VirtualSize
        {
            get { return _virtualSize; }
        }

        public override long PhysicalSize
        {
            get { return _physicalSize; }
        }

    }

    [ProtoContract]
    public class DiffStream : BackupStream
    {
        public override StreamType Type
        {
            get { return StreamType.Diff; }
        }

        private long _virtualSize = -1;

        private long _physicalSize = -1;

        public struct Block
        {
            public ulong StreamIdSource;
            public long OffsetInSource;
            public long Size;
        }

        public List<Block> Blocks = new List<Block>();

        public override long VirtualSize
        {
            get
            {
                return _virtualSize != -1 ?_virtualSize : (_virtualSize = Blocks.Sum(x => x.Size));
            }
        }

        public override long PhysicalSize
        {
            get
            {
                return _physicalSize != -1 ? _physicalSize : (_physicalSize = Blocks.Where(x => x.StreamIdSource == UniqueId).Sum(x => x.Size));
            }
        }

    }


}
