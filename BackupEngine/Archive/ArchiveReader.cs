using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackupEngine.FileSystem;
using BackupEngine.Serialization;
using BackupEngine.Util;
using BackupEngine.Util.Streams;

namespace BackupEngine.Archive
{
    public class ArchiveReader : Archive
    {
        private FileStream _stream;
        private List<ulong> _streamIds = new List<ulong>();
        private List<long> _streamSizes = new List<long>();

        public ArchiveReader(string existingPath)
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
            using (var filteredStream = DoInputFiltering(_stream, false))
                item1 = Serializer.Deserialize<VersionManifest>(filteredStream);
            _streamIds = new List<ulong>(item1.ArchiveMetadata.StreamIds);
            _streamSizes = new List<long>(item1.ArchiveMetadata.StreamSizes);
            var item2 = new List<FileSystemObject>();
            start -= item1.EntriesSizeInArchive;
            _stream.Seek(start, SeekOrigin.End);
            using (var filteredStream = DoInputFiltering(_stream))
                item2.AddRange(
                    item1.ArchiveMetadata.EntrySizes
                        .Select(entrySize => new BoundedStream(filteredStream, entrySize))
                        .Select(Serializer.Deserialize<FileSystemObject>)
                    );
            return new Tuple<VersionManifest, List<FileSystemObject>>(item1, item2);
        }

        public void Begin(Action<ulong, Stream> callback)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            using (var filteredStream = DoInputFiltering(_stream))
            {
                _streamIds.ZipWith(_streamSizes, (id, size) => 
                {
                    // ReSharper disable once AccessToDisposedClosure
                    var bounded = new BoundedStream(filteredStream, size);
                    callback(id, bounded);
                });
            }
        }

        public override void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }
}
