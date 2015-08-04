﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using ProtoBuf;

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
