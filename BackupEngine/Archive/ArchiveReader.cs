using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        public VersionManifest VersionManifest;
        public List<FileSystemObject> BaseObjects;
        private long _manifestOffset = -1;
        private long _manifestSize = -1;
        private long _baseObjectsOffset = -1;

        public ArchiveReader(string existingPath)
        {
            _stream = new FileStream(existingPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public VersionManifest ReadManifest()
        {
            if (_manifestOffset < 0)
            {
                long start = -8 - 256/8;
                _stream.Seek(start, SeekOrigin.End);
                var temp = new byte[8];
                var bytesRead = _stream.Read(temp, 0, 8);
                if (bytesRead != temp.Length)
                    throw new InvalidDataException();
                _manifestSize = BitConverter.ToInt64(temp, 0);
                start -= _manifestSize;
                _manifestOffset = _stream.Seek(start, SeekOrigin.End);
            }
            else
                _stream.Seek(_manifestOffset, SeekOrigin.Begin);
            using (var boundedStream = new BoundedStream(_stream, _manifestSize))
            using (var filteredStream = DoInputFiltering(boundedStream, false))
                VersionManifest = Serializer.Deserialize<VersionManifest>(filteredStream);
            _baseObjectsOffset = _manifestOffset - VersionManifest.ArchiveMetadata.EntriesSizeInArchive;
            _streamIds = new List<ulong>(VersionManifest.ArchiveMetadata.StreamIds);
            _streamSizes = new List<long>(VersionManifest.ArchiveMetadata.StreamSizes);
            return VersionManifest;
        }

        public List<FileSystemObject> ReadBaseObjects()
        {
            if (VersionManifest == null)
                ReadManifest();
            Debug.Assert(VersionManifest != null);
            BaseObjects = new List<FileSystemObject>();
            _stream.Seek(_baseObjectsOffset, SeekOrigin.Begin);
            using (var boundedStream = new BoundedStream(_stream, _manifestOffset - _baseObjectsOffset))
            using (var filteredStream = DoInputFiltering(boundedStream))
                BaseObjects.AddRange(
                    VersionManifest.ArchiveMetadata.EntrySizes
                        .Select(entrySize => new BoundedStream(filteredStream, entrySize))
                        .Select(Serializer.Deserialize<FileSystemObject>)
                    );
            return BaseObjects;
        }

        public List<FileSystemObject> GetBaseObjects()
        {
            var ret = ReadBaseObjects();
            BaseObjects = null;
            return ret;
        }

        public void Begin(Action<ulong, Stream> callback)
        {
            if (VersionManifest == null)
                ReadManifest();
            _stream.Seek(0, SeekOrigin.Begin);
            using (var filteredStream = DoInputFiltering(_stream))
            {
                _streamIds.Zip(_streamSizes, (id, size) => 
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
