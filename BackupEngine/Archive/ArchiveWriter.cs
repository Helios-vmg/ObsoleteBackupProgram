using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Serialization;
using BackupEngine.Util.Streams;

namespace BackupEngine.Archive
{
    public class ArchiveWriter : Archive
    {
        private enum ArchiveState
        {
            Initial = 0,
            PushingFiles = 1,
            PushingFsos = 2,
            Final = 3,
        }

        private FileStream _fileStream;
        private Stream _hashedStream;
        private Filter _filter;
        private ArchiveState _state = ArchiveState.Initial;
        private readonly List<ulong> _streamIds = new List<ulong>();
        private readonly List<long> _streamSizes = new List<long>();
        private readonly List<long> _baseObjectEntrySizes = new List<long>();
        private HashAlgorithm _hash;

        private HashAlgorithm NewHash()
        {
            return _hash = SHA256.Create();
        }

        private void EnsureMinimumState(ArchiveState minState)
        {
            if ((int)_state < (int)minState)
                throw new InvalidProgramState();
        }

        private void EnsureMaximumState(ArchiveState maxState)
        {
            if ((int)_state > (int)maxState)
                throw new InvalidProgramState();
        }

        public ArchiveWriter(string newPath)
        {
            _fileStream = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _hashedStream = new HashCalulatorOutputFilter(_fileStream, NewHash());
        }

        public void AddFile(ulong streamId, Stream file)
        {
            EnsureMaximumState(ArchiveState.PushingFiles);
            if (_state == ArchiveState.Initial)
            {
                _filter = DoFiltering(_hashedStream);
                _state = ArchiveState.PushingFiles;
            }
            _streamIds.Add(streamId);
            _streamSizes.Add(file.Length);
            file.CopyTo(_filter);
        }

        public void AddFso(FileSystemObject fso)
        {
            EnsureMinimumState(ArchiveState.PushingFiles);
            EnsureMaximumState(ArchiveState.PushingFsos);
            if (_state == ArchiveState.PushingFiles)
            {
                _filter.Flush();
                _filter.Dispose();
                _filter = DoFiltering(_hashedStream);
                _state = ArchiveState.PushingFsos;
            }
            var x0 = _filter.BytesIn;
            Serializer.SerializeToStream(_filter, fso);
            var x1 = _filter.BytesIn;
            _baseObjectEntrySizes.Add(x1 - x0);
        }

        public void AddVersionManifest(VersionManifest versionManifest)
        {
            EnsureMinimumState(ArchiveState.PushingFsos);
            EnsureMaximumState(ArchiveState.PushingFsos);
            _filter.Flush();
            _filter.Dispose();
            _filter = null;

            versionManifest.ArchiveMetadata = new ArchiveMetadata
            {
                EntrySizes = new List<long>(_baseObjectEntrySizes),
                StreamIds = new List<ulong>(_streamIds),
                StreamSizes = new List<long>(_streamSizes),
                CompressionMethod = CompressionMethod,
            };

            long manifestLength;
            using (var filter = DoFiltering(_hashedStream, false))
            {
                var x0 = filter.BytesIn;
                Serializer.SerializeToStream(filter, versionManifest);
                var x1 = filter.BytesIn;
                manifestLength = x1 - x0;
                filter.Flush();
            }
            var bytes = BitConverter.GetBytes(manifestLength);
            _hashedStream.Write(bytes, 0, bytes.Length);
            _hashedStream.Dispose();
            _hashedStream = null;
            _hash.TransformFinalBlock(new byte[0], 0, 0);
            bytes = _hash.Hash;
            _fileStream.Write(bytes, 0, bytes.Length);
            _fileStream.Flush();
            _fileStream.Dispose();
            _fileStream = null;
            _state = ArchiveState.Final;
        }

        public override void Dispose()
        {
            if (_filter != null)
            {
                _filter.Dispose();
                _filter = null;
            }
            if (_hashedStream != null)
            {
                _hashedStream.Dispose();
                _hashedStream = null;
            }
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
        }
    }
}
