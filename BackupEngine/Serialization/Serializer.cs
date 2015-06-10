using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BackupEngine.Serialization
{
    public class OrderedContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var list = base.CreateProperties(type, memberSerialization).ToList();
            list.Sort((x, y) =>
            {
                if (x.PropertyName == "Children")
                    return y.PropertyName != "Children" ? 1 : 0;
                if (y.PropertyName == "Children")
                    return -1;
                return String.Compare(x.PropertyName, y.PropertyName, StringComparison.InvariantCulture);
            });
            return list;
        }
    }

    public static class Serializer
    {
        public static string Serialize(FileSystemObject o)
        {
            return JsonConvert.SerializeObject(o, Formatting.Indented, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new OrderedContractResolver(),
                TypeNameHandling = TypeNameHandling.All,
            });
        }

        public static FileSystemObject Deserialize(byte[] buffer)
        {
            return (FileSystemObject)JsonConvert.DeserializeObject(Encoding.UTF8.GetString(buffer), new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }

        public static MemoryStream SerializeToStream(FileSystemObject o)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(Serialize(o)));
        }

        public static string Serialize(VersionManifest o)
        {
            return JsonConvert.SerializeObject(o, Formatting.Indented);
        }

        public static MemoryStream SerializeToStream(VersionManifest o)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(Serialize(o)));
        }

    }
}
