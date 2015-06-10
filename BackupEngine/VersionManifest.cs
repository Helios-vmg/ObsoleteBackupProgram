using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BackupEngine
{
    [JsonObject(MemberSerialization.OptOut)]
    public class VersionManifest
    {
        public DateTime CreationTime;
        public uint VersionNumber;
        public int DependentOnVersion = -1;
        public int EntryCount;
    }
}
