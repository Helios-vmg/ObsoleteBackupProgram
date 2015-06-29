using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Util;
using Ionic.Zip;

namespace BackupEngine
{
    internal class VersionForRestore : IDisposable
    {
        private BaseBackupEngine _engine;
        public readonly int VersionNumber;
        public readonly VersionManifest Manifest;
        public readonly string Path;
        public readonly ZipFile Zip;
        private readonly Dictionary<int, VersionForRestore> _dependencies = new Dictionary<int, VersionForRestore>();
        private List<FileSystemObject> _baseObjects = null;
        private List<List<BackupStream>> _streams = null;
        private Dictionary<ulong, BackupStream> _streamsDict = null;

        public VersionForRestore(int version, BaseBackupEngine engine)
        {
            _engine = engine;
            VersionNumber = version;
            Path = engine.GetVersionPath(version);
            Zip = new ZipFile(Path);
            Manifest = engine.OpenVersionManifest(Zip, Path);
        }

        public void Dispose()
        {
            Zip.Dispose();
            _dependencies.Values.ForEach(x => x.Dispose());
        }

        public List<FileSystemObject> BaseObjects
        {
            get { return _baseObjects ?? (_baseObjects = GetBaseObjects()); }
        }

        public List<List<BackupStream>> BackupStreams
        {
            get { return _streams ?? (_streams = GetBackupStreams()); }
        }

        public Dictionary<ulong, BackupStream> StreamDict
        {
            get { return _streamsDict ?? (_streamsDict = GetStreamsDict()); }
        }

        private List<FileSystemObject> GetBaseObjects()
        {
            var ret = new List<FileSystemObject>();
            for (var i = 0; i < Manifest.EntryCount; i++)
                ret.Add(_engine.OpenBaseObject(Zip, i));
            return ret;
        }

        private List<List<BackupStream>> GetBackupStreams()
        {
            var ret = new List<List<BackupStream>>();
            for (var i = 0; i < Manifest.EntryCount; i++)
                ret.Add(_engine.OpenBackupStream(Zip, i));
            return ret;
        }

        private Dictionary<ulong, BackupStream> GetStreamsDict()
        {
            var ret = new Dictionary<ulong, BackupStream>();
            var streams = GetBackupStreams();
            var objects = GetBaseObjects();
            streams.ForEach(x => x.ForEach(y => ret[y.UniqueId] = y));
            objects.ForEach(x => x.Iterate(y =>
            {
                BackupStream stream;
                if (ret.TryGetValue(y.StreamUniqueId, out stream))
                {
                    y.BackupStream = stream;
                    stream.FileSystemObjects.Add(y);
                }
            }));
            return ret;
        }

        public void FillDependencies(Dictionary<int, VersionForRestore> dict)
        {
            Manifest.VersionDependencies.ForEach(x => _dependencies[x] = dict[x]);
            _dependencies.Values.ForEach(x => x.FillDependencies(dict));
        }

        public void RestorePath(string path)
        {
            var obj = GetFso(path);
            obj.DeleteExisting();
            if (!obj.Restore())
            {
                var stream = GetStream(obj);
                obj.Restore(stream);
            }
        }

        private FileSystemObject GetFso(string path)
        {
            var _this = this;
            while (true)
            {
                var obj = _this._baseObjects.Select(x => x.Find(path)).FirstOrDefault();
                if (obj == null)
                    throw new InvalidBackup(Path, "Couldn't locate object \"" + path + "\", even though the metadata states it should be there.");
                if (obj.LatestVersion == _this.VersionNumber)
                    return obj;
                _this = _this._dependencies[obj.LatestVersion];
            }
        }

        private Stream GetStream(FileSystemObject fso)
        {
            GetStreamsDict();
            BackupStream backupStream = fso.BackupStream;
            if (backupStream == null)
                throw new InvalidBackup(Path, "Couldn't locate stream for object \"" + fso.PathWithoutBase + "\", even though the metadata states it should be there.");
            return backupStream.GetStream(this);
        }
    }
}
