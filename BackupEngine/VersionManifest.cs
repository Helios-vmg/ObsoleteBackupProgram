using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public long EntriesSizeInArchive;
        public ulong FirstStreamUniqueId;
        public ulong NextStreamUniqueId;
        public ulong FirstDifferentialChainUniqueId;
        public ulong NextDifferentialChainUniqueId;

        public ArchiveMetadata ArchiveMetadata;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ArchiveMetadata
    {
        public List<long> EntrySizes;
        public List<ulong> StreamIds;
        public List<long> StreamSizes;

        public enum CompressionMethodType
        {
            None,
            GZip,
            BZip2,
            Lzma,
        }

        public CompressionMethodType CompressionMethod;
    }
}
