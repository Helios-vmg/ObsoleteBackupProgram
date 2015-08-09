using System;
using System.Collections.Generic;
using ProtoBuf;

namespace BackupEngine
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class VersionManifest
    {
        public DateTime CreationTime;
        public int VersionNumber;
        public List<int> VersionDependencies = new List<int>();
        public int EntryCount;
        public ulong FirstStreamUniqueId;
        public ulong NextStreamUniqueId;
        public ulong FirstDifferentialChainUniqueId;
        public ulong NextDifferentialChainUniqueId;

        public ArchiveMetadata ArchiveMetadata;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ArchiveMetadata
    {
        public long EntriesSizeInArchive;
        public List<long> EntrySizes;
        public List<ulong> StreamIds;
        public List<long> StreamSizes;

        public void EnsureNonNull()
        {
            EntrySizes = EntrySizes ?? new List<long>();
            StreamIds = StreamIds ?? new List<ulong>();
            StreamSizes = StreamSizes ?? new List<long>();
        }

        //public enum CompressionMethodType
        //{
        //    None,
        //    GZip,
        //    BZip2,
        //    Lzma,
        //}
        //
        //public CompressionMethodType CompressionMethod;
    }
}
