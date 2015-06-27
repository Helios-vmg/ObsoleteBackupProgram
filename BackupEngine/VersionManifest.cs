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
        public ulong FirstStreamUniqueId;
        public ulong NextStreamUniqueId;
        public ulong FirstDifferentialChainUniqueId;
        public ulong NextDifferentialChainUniqueId;
    }
}
