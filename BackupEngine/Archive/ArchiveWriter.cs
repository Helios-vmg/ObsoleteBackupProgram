using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Serialization;
using BackupEngine.Util;
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
        private OutputFilter _outputFilter;
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
            _hashedStream = new HashCalculatorOutputFilter(_fileStream, NewHash());
        }

        public void AddFile(ulong streamId, Stream file)
        {
            EnsureMaximumState(ArchiveState.PushingFiles);
            if (_state == ArchiveState.Initial)
            {
                _outputFilter = DoOutputFiltering(_hashedStream);
                _state = ArchiveState.PushingFiles;
            }
            _streamIds.Add(streamId);
            _streamSizes.Add(file.Length);
            file.CopyTo(_outputFilter);
        }

        public void AddFso(FileSystemObject fso)
        {
            EnsureMinimumState(ArchiveState.PushingFiles);
            EnsureMaximumState(ArchiveState.PushingFsos);
            if (_state == ArchiveState.PushingFiles)
            {
                _outputFilter.Flush();
                _outputFilter.Dispose();
                _outputFilter = DoOutputFiltering(_hashedStream);
                _state = ArchiveState.PushingFsos;
            }
            var x0 = _outputFilter.BytesWritten;
            Serializer.SerializeToStream(_outputFilter, fso);
            var x1 = _outputFilter.BytesWritten;
            _baseObjectEntrySizes.Add(x1 - x0);
        }

        public void AddVersionManifest(VersionManifest versionManifest)
        {
            EnsureMinimumState(ArchiveState.PushingFsos);
            EnsureMaximumState(ArchiveState.PushingFsos);
            _outputFilter.Flush();
            _outputFilter.Dispose();
            _outputFilter = null;

            versionManifest.ArchiveMetadata = new ArchiveMetadata
            {
                EntrySizes = new List<long>(_baseObjectEntrySizes),
                StreamIds = new List<ulong>(_streamIds),
                StreamSizes = new List<long>(_streamSizes),
                CompressionMethod = CompressionMethod,
            };

            long manifestLength;
            using (var filter = DoOutputFiltering(_hashedStream, false))
            {
                var x0 = filter.BytesWritten;
                Serializer.SerializeToStream(filter, versionManifest);
                var x1 = filter.BytesWritten;
                manifestLength = x1 - x0;
                filter.Flush();
            }
            var bytes = BitConverter.GetBytes(manifestLength);
            _hashedStream.Write(bytes, 0, bytes.Length);
            _hashedStream.Dispose();
            _hashedStream = null;
            _hash.FinishHashing();
            bytes = _hash.Hash;
            _fileStream.Write(bytes, 0, bytes.Length);
            _fileStream.Flush();
            _fileStream.Dispose();
            _fileStream = null;
            _state = ArchiveState.Final;
        }

        public override void Dispose()
        {
            if (_outputFilter != null)
            {
                _outputFilter.Dispose();
                _outputFilter = null;
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
