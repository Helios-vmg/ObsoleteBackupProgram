﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackupEngine.FileSystem;
using ProtoBuf;

namespace BackupEngine
{
    public enum StreamType
    {
        None,
        Unmodified,
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
        public readonly List<FileSystemObject> FileSystemObjects = new List<FileSystemObject>();
        public bool Restored { get; protected set; }

        public abstract long VirtualSizeProp { get; }
        public abstract long PhysicalSizeProp { get; }

        public abstract void GetDependencies(HashSet<int> versionDependencies);
        internal abstract Stream GetStream(VersionForRestore version);

        public virtual bool HasData { get { return true; } }
    }

    [ProtoContract]
    public class UnmodifiedStream : BackupStream
    {
        [ProtoMember(5)]
        public long VirtualSize;
        [ProtoMember(6)]
        public int ContainingVersion;

        public override StreamType Type
        {
            get { return StreamType.Unmodified; }
        }

        public override long VirtualSizeProp
        {
            get { return VirtualSize; }
        }

        public override long PhysicalSizeProp
        {
            get { return 0; }
        }

        public override void GetDependencies(HashSet<int> versionDependencies)
        {
            versionDependencies.Add(ContainingVersion);
        }

        internal override Stream GetStream(VersionForRestore version)
        {
            return null;
        }
        public override bool HasData { get { return false; } }
    }

    [ProtoContract]
    public class FullStream : BackupStream
    {
        public override StreamType Type
        {
            get { return StreamType.Full; }
        }

        [ProtoMember(2)]
        public long VirtualSize;
        [ProtoMember(3)]
        public long PhysicalSize;

        public override long VirtualSizeProp
        {
            get { return VirtualSize; }
        }

        public override long PhysicalSizeProp
        {
            get { return PhysicalSize; }
        }

        public override void GetDependencies(HashSet<int> versionDependencies){}

        internal override Stream GetStream(VersionForRestore version)
        {
            return null;
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

        public override long VirtualSizeProp
        {
            get
            {
                return _virtualSize != -1 ?_virtualSize : (_virtualSize = Blocks.Sum(x => x.Size));
            }
        }

        public override long PhysicalSizeProp
        {
            get
            {
                return _physicalSize != -1 ? _physicalSize : (_physicalSize = Blocks.Where(x => x.StreamIdSource == UniqueId).Sum(x => x.Size));
            }
        }

        public override void GetDependencies(HashSet<int> versionDependencies)
        {
            throw new NotImplementedException();
        }

        internal override Stream GetStream(VersionForRestore version)
        {
            throw new NotImplementedException();
        }
    }


}
