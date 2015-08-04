using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using BackupEngine.Util;
using Ionic.BZip2;

namespace BackupEngine.Archive
{
    public abstract class Archive
    {
    }

    public class ArchiveRead : Archive
    {
        private readonly FileStream _stream;
        private List<ulong> _streamIds = new List<ulong>();
        private List<long> _streamSizes = new List<long>();

        public ArchiveRead(string existingPath)
        {
            _stream = new FileStream(existingPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public Tuple<VersionManifest, List<FileSystemObject>> ReadFooter()
        {
            long start = -8;
            _stream.Seek(start, SeekOrigin.End);
            var temp = new byte[8];
            var bytesRead = _stream.Read(temp, 0, 8);
            if (bytesRead != temp.Length)
                throw new InvalidDataException();
            start -= BitConverter.ToInt64(temp, 0);
            _stream.Seek(start, SeekOrigin.End);
            VersionManifest item1;
            using (var bZip2InputStream = new BZip2InputStream(_stream, true))
                item1 = Serialization.Serializer.Deserialize<VersionManifest>(bZip2InputStream);
            _streamIds = new List<ulong>(item1.StreamIds);
            _streamSizes = new List<long>(item1.StreamSizes);
            var item2 = new List<FileSystemObject>();
            start -= item1.EntriesSizeInArchive;
            _stream.Seek(start, SeekOrigin.End);
            using (var bZip2InputStream = new BZip2InputStream(_stream, true))
                item2.AddRange(
                    item1.EntrySizes
                        .Select(entrySize => new BoundedStream(bZip2InputStream, entrySize))
                        .Select(Serialization.Serializer.Deserialize<FileSystemObject>)
                );
            return new Tuple<VersionManifest, List<FileSystemObject>>(item1, item2);
        }

        public void Begin(Action<ulong, Stream> callback)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            using (var bZip2InputStream = new BZip2InputStream(_stream, true))
            {
                _streamIds.ZipWith(_streamSizes, (id, size) => 
                {
                    Debug.Assert(bZip2InputStream != null, "bZip2InputStream != null");
                    var bounded = new BoundedStream(bZip2InputStream, size);
                    callback(id, bounded);
                });
            }
        }
    }

    public class ArchiveWrite : Archive
    {
        private Queue<ulong> _queuedEntries = new Queue<ulong>();

    }
}
