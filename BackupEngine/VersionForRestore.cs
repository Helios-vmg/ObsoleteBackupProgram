using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BackupEngine.Archive;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Util;

namespace BackupEngine
{
    internal class VersionForRestore : IDisposable
    {
        private BaseBackupEngine _engine;
        public readonly int VersionNumber;
        public readonly VersionManifest Manifest;
        public readonly string Path;
        public readonly ArchiveReader Archive;
        private readonly Dictionary<int, VersionForRestore> _dependencies = new Dictionary<int, VersionForRestore>();
        private List<FileSystemObject> _baseObjects;
        private List<List<BackupStream>> _streams;
        private Dictionary<ulong, BackupStream> _streamsDict;

        public VersionForRestore(int version, BaseBackupEngine engine)
        {
            _engine = engine;
            VersionNumber = version;
            Path = engine.GetVersionPath(version);
            Archive = new ArchiveReader(Path);
            Manifest = Archive.ReadManifest();
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Archive.Dispose();
            _dependencies.Values.ForEach(x => x.Dispose());
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public List<FileSystemObject> BaseObjects
        {
            get { return _baseObjects ?? (_baseObjects = GetBaseObjects()); }
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public List<List<BackupStream>> BackupStreams
        {
            get { return _streams ?? (_streams = GetBackupStreams()); }
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public Dictionary<ulong, BackupStream> StreamDict
        {
            get { return _streamsDict ?? (_streamsDict = GetStreamsDict()); }
        }

        private List<FileSystemObject> GetBaseObjects()
        {
            return Archive.GetBaseObjects();
        }

        private List<List<BackupStream>> GetBackupStreams()
        {
            var ret = new List<List<BackupStream>>();
            return ret;
        }

        private Dictionary<ulong, BackupStream> GetStreamsDict()
        {
            var ret = new Dictionary<ulong, BackupStream>();
            BackupStreams.ForEach(x => x.ForEach(y => ret[y.UniqueId] = y));
            return ret;
        }

        private bool _dependenciesFull;

        public void FillDependencies(Dictionary<int, VersionForRestore> dict)
        {
            if (_dependenciesFull)
                return;
            Manifest.VersionDependencies.ForEach(x => _dependencies[x] = dict[x]);
            _dependencies.Values.ForEach(x => x.FillDependencies(dict));
            _dependenciesFull = true;
        }

        public void SetAllStreamIds()
        {
            var requiredObjects = new Dictionary<int, List<FileSystemObject>>();
            foreach (var baseObject in BaseObjects)
            {
                baseObject.Iterate(fso =>
                {
                    if (fso.StreamUniqueId != BaseBackupEngine.InvalidStreamId || fso.LatestVersion < 0)
                        return;
                    List<FileSystemObject> list;
                    if (!requiredObjects.TryGetValue(fso.LatestVersion, out list))
                        requiredObjects[fso.LatestVersion] = list = new List<FileSystemObject>();
                    list.Add(fso);
                });
            }
            foreach (var requiredObject in requiredObjects.OrderBy(x => x.Key))
            {
                Console.WriteLine("Reading version " + requiredObject.Key);
                var objects = _dependencies[requiredObject.Key].GetBaseObjects();
                foreach (var fso in requiredObject.Value)
                {
                    var found = objects.Select(x => x.Find(fso.MappedPath)).FirstOrDefault();
                    if (found == null || found.StreamUniqueId == BaseBackupEngine.InvalidStreamId)
                        continue;
                    fso.StreamUniqueId = found.StreamUniqueId;
                }
            }
        }

        public void Restore(FileSystemObject fso, List<FileSystemObject> restoreLater)
        {
            fso.DeleteExisting();
            if (!fso.Restore() && fso.LatestVersion >= 0)
                restoreLater.Add(fso);
        }

        private FileSystemObject GetFso(string path)
        {
            var _this = this;
            while (true)
            {
                var obj = _this.BaseObjects.Select(x => x.Find(path)).FirstOrDefault();
                if (obj == null)
                    throw new InvalidBackup(Path, "Couldn't locate object \"" + path + "\", even though the metadata states it should be there.");
                if (obj.LatestVersion == _this.VersionNumber || obj.LatestVersion < 0)
                    return obj;
                _this = _this._dependencies[obj.LatestVersion];
            }
        }

        private Stream GetStream(FileSystemObject fso)
        {
            if (fso.LatestVersion != VersionNumber)
            {
                VersionForRestore version;
                if (!_dependencies.TryGetValue(fso.LatestVersion, out version))
                    throw new InvalidBackup(Path, "Couldn't locate stream for object \"" + fso.PathWithoutBase + "\", even though the metadata states it should be there.");
                return version.GetStream(fso);
            }
            var dict = StreamDict;
            BackupStream backupStream;
            if (!dict.TryGetValue(fso.StreamUniqueId, out backupStream))
                throw new InvalidBackup(Path, "Couldn't locate stream for object \"" + fso.PathWithoutBase + "\", even though the metadata states it should be there.");
            return backupStream.GetStream(this);
        }
    }
}
