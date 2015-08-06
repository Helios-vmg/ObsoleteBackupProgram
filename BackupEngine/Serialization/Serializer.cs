using System.IO;

namespace BackupEngine.Serialization
{
    public static class Serializer
    {
        public static void SerializeToStream<T>(Stream dst, T o)
        {
            ProtoBuf.Serializer.Serialize(dst, o);
        }
        
        public static byte[] Serialize<T>(T o)
        {
            var mem = new MemoryStream();
            SerializeToStream(mem, o);
            return mem.ToArray();
        }

        public static Stream SerializeToStream<T>(T o)
        {
            var mem = new MemoryStream();
            SerializeToStream(mem, o);
            return mem;
        }

        public static T Deserialize<T>(byte[] buffer)
        {
            var mem = new MemoryStream(buffer);
            return ProtoBuf.Serializer.Deserialize<T>(mem);
        }

        public static T Deserialize<T>(Stream stream)
        {
            return ProtoBuf.Serializer.Deserialize<T>(stream);
        }
    }
}
