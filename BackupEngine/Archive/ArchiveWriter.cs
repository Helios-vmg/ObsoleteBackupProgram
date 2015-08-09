using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Alphaleonis.Win32.Filesystem;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Serialization;
using BackupEngine.Util;
using BackupEngine.Util.Streams;
using File = Alphaleonis.Win32.Filesystem.File;

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

        private KernelTransaction _transaction;
        private FileStream _fileStream;
        private OutputFilter _hashedStream;
        private OutputFilter _outputFilter;
        private ArchiveState _state = ArchiveState.Initial;
        private readonly List<ulong> _streamIds = new List<ulong>();
        private readonly List<long> _streamSizes = new List<long>();
        private readonly List<long> _baseObjectEntrySizes = new List<long>();
        private HashAlgorithm _hash;
        private long _initialFsoOffset;
        public bool AnyFile { get; private set; }

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
            _transaction = new KernelTransaction();
            _fileStream = File.OpenTransacted(_transaction, newPath, FileMode.Create, FileAccess.Write, FileShare.None);
            _hashedStream = new HashCalculatorOutputFilter(_fileStream, NewHash());
            
        }

        public byte[] AddFile(ulong streamId, Stream file, HashType type = HashType.None)
        {
            EnsureMaximumState(ArchiveState.PushingFiles);
            if (_state == ArchiveState.Initial)
            {
                _outputFilter = DoOutputFiltering(_hashedStream);
                _state = ArchiveState.PushingFiles;
            }
            byte[] ret = null;
            _streamIds.Add(streamId);
            _streamSizes.Add(file.Length);
            Stream stream = file;
            HashAlgorithm hash = null;
            if (type != HashType.None)
            {
                hash = Hash.New(type);
                stream = new HashCalculatorInputFilter(stream, hash);
            }
            stream.CopyTo(_outputFilter);
            if (type != HashType.None)
            {
                stream.Dispose();
                hash.FinishHashing();
                ret = hash.Hash;
            }
            AnyFile = true;
            return ret;
        }

        public void AddFso(FileSystemObject fso)
        {
            EnsureMaximumState(ArchiveState.PushingFsos);
            if (_state == ArchiveState.Initial)
            {
                _initialFsoOffset = _hashedStream.BytesWritten;
                _outputFilter = DoOutputFiltering(_hashedStream);
                _state = ArchiveState.PushingFsos;
            }
            else if (_state == ArchiveState.PushingFiles)
            {
                _outputFilter.Flush();
                _outputFilter.Dispose();
                _initialFsoOffset = _hashedStream.BytesWritten;
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
                //CompressionMethod = CompressionMethod,
                EntriesSizeInArchive = _hashedStream.BytesWritten - _initialFsoOffset,
            };

            long manifestLength;
            using (var filter = DoOutputFiltering(_hashedStream, false))
            {
                var x0 = _hashedStream.BytesWritten;
                Serializer.SerializeToStream(filter, versionManifest);
                filter.Flush();
                var x1 = _hashedStream.BytesWritten;
                manifestLength = x1 - x0;
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
            _transaction.Commit();
            _fileStream = null;
            _transaction.Dispose();
            _transaction = null;
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
            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }
    }
}
