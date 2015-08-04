using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Serialization;
using BackupEngine.Util;
using Ionic.BZip2;

namespace BackupEngine.Archive
{
    public abstract class Archive : IDisposable
    {
        private readonly List<FilterGenerator> _filters = new List<FilterGenerator>();

        public abstract void Dispose();

        public virtual void AddFilterGenerator(FilterGenerator fg)
        {
            _filters.Add(fg);
        }

        protected Stream DoFiltering(Stream stream)
        {
            bool first = true;
            foreach (var filterGenerator in _filters)
            {
                stream = filterGenerator.Filter(stream, first);
                first = false;
            }
            return stream;
        }
    }

    public abstract class FilterGenerator
    {
        public abstract Stream Filter(Stream stream, bool leaveOpen);
    }

    public class ArchiveRead : Archive
    {
        private FileStream _stream;
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
            using (var filteredStream = DoFiltering(_stream))
                item1 = Serialization.Serializer.Deserialize<VersionManifest>(filteredStream);
            _streamIds = new List<ulong>(item1.StreamIds);
            _streamSizes = new List<long>(item1.StreamSizes);
            var item2 = new List<FileSystemObject>();
            start -= item1.EntriesSizeInArchive;
            _stream.Seek(start, SeekOrigin.End);
            using (var filteredStream = DoFiltering(_stream))
                item2.AddRange(
                    item1.EntrySizes
                        .Select(entrySize => new BoundedStream(filteredStream, entrySize))
                        .Select(Serialization.Serializer.Deserialize<FileSystemObject>)
                );
            return new Tuple<VersionManifest, List<FileSystemObject>>(item1, item2);
        }

        public void Begin(Action<ulong, Stream> callback)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            using (var filteredStream = DoFiltering(_stream))
            {
                _streamIds.ZipWith(_streamSizes, (id, size) => 
                {
                    Debug.Assert(filteredStream != null);
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

    public class ArchiveWrite : Archive
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
        private Stream _filteredStream;
        private ArchiveState _state = ArchiveState.Initial;
        private List<ulong> _streamIds = new List<ulong>();
        private List<long> _streamSizes = new List<long>();
        private List<long> _baseObjectEntrySizes = new List<long>();
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

        public ArchiveWrite(string newPath)
        {
            _fileStream = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None)
            _hashedStream = new HashCalulatorOutputFilter(_fileStream, NewHash());
        }

        public void AddFile(ulong streamId, Stream file)
        {
            EnsureMaximumState(ArchiveState.PushingFiles);
            if (_state == ArchiveState.Initial)
            {
                _filteredStream = DoFiltering(_hashedStream);
                _state = ArchiveState.PushingFiles;
            }
            _streamIds.Add(streamId);
            _streamSizes.Add(file.Length);
            file.CopyTo(_filteredStream);
        }

        public void PushFso(FileSystemObject fso)
        {
            EnsureMinimumState(ArchiveState.PushingFiles);
            EnsureMaximumState(ArchiveState.PushingFsos);
            if (_state == ArchiveState.PushingFiles)
            {
                _filteredStream.Flush();
                _filteredStream.Dispose();
                _filteredStream = DoFiltering(_hashedStream);
                _state = ArchiveState.PushingFsos;
            }
            var output = Serializer.SerializeToStream(fso);
            _baseObjectEntrySizes.Add(output.Length);
            output.CopyTo(_filteredStream);
        }

        /**/

        public override void Dispose()
        {
            if (_filteredStream != null)
            {
                _filteredStream.Dispose();
                _filteredStream = null;
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
