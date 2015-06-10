﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.Serialization;
using BackupEngine.Util;
using Newtonsoft.Json;

namespace BackupEngine.FileSystem
{
    public enum FileSystemObjectType
    {
        Directory,
        RegularFile,
        DirectorySymlink,
        Junction,
        FileSymlink,
        FileReparsePoint,
        FileHardlink,
    }

    public enum HashType
    {
        Crc32,
        Sha1,
        Md5,
        Sha256,
    }

    public class FileSystemObjectSettings
    {
        public IErrorReporter Reporter = null;
        public Func<FileSystemObject, BackupMode> BackupModeMap = null;
    }

    [JsonObject(MemberSerialization.OptOut)]
    public abstract class FileSystemObject
    {
        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path) ?? string.Empty;
        }

        [JsonIgnore]
        public abstract FileSystemObjectType Type { get; }

        public string Name;

        [JsonIgnore]
        public FileSystemObject Parent;

        public string BasePath;

        [JsonIgnore]
        public string PathWithoutBase
        {
            get
            {
                var _this = this;
                var temp = new List<string>();
                var expectedLength = 0;
                while (true)
                {
                    temp.Add(_this.Name);
                    expectedLength += _this.Name.Length + 1;
                    if (_this.Parent == null)
                        break;
                    _this = _this.Parent;
                }
                var ret = new StringBuilder(expectedLength);
                for (var i = temp.Count; i-- != 0; )
                {
                    if (ret.Length != 0)
                        ret.Append(@"\");
                    ret.Append(temp[i]);
                }
                return ret.ToString();
            }
        }

        [JsonIgnore]
        public string Path
        {
            get
            {
                var _this = this;
                var temp = new List<string>();
                var expectedLength = 0;
                while (true)
                {
                    temp.Add(_this.Name);
                    expectedLength += _this.Name.Length + 1;
                    if (_this.Parent == null)
                        break;
                    _this = _this.Parent;
                }
                temp.Add(_this.BasePath);
                expectedLength += _this.BasePath.Length + 1;
                var ret = new StringBuilder(expectedLength);
                for (var i = temp.Count; i-- != 0;)
                {
                    if (ret.Length != 0)
                        ret.Append(@"\");
                    ret.Append(temp[i]);
                }
                return ret.ToString();
            }
        }

        public string Target;

        public long Size;

        [JsonIgnore]
        public virtual long RecursiveSize
        {
            get
            {
                if (BackupMode == BackupMode.NoBackup)
                    return 0;
                return Size;
            }
        }

        public DateTime ModificationTime;

        public Guid? UniqueId;

        //[JsonDictionary()]
        public Dictionary<HashType, byte[]> Hashes = new Dictionary<HashType, byte[]>();

        //Does not compute the hash if hasn't been computed already.
        public virtual byte[] GetHash(HashType type)
        {
            return Hashes.GetValueOrDefault(type, null);
        }

        //Computes the hash if necessary.
        public abstract byte[] ComputeHash(HashType type);

        /*public static FileSystemObjectType GetType(Guid fileSystemObjectId)
        {
            throw new NotImplementedException();
        }*/

        public List<string> Exceptions = new List<string>();

        public BackupMode BackupMode;

        private void AddException(Exception e)
        {
            if (Exceptions == null)
                Exceptions = new List<string>();
            Exceptions.Add(e.Message);
        }

        public static FileSystemObjectType GetType(string path)
        {
            return FileSystemOperations.GetFileSystemObjectType(path);
        }

        public static FileSystemObject Create(string path, FileSystemObjectSettings settings = null)
        {
            switch (GetType(path))
            {
                case FileSystemObjectType.Directory:
                    return new DirectoryFso(path, settings);
                case FileSystemObjectType.RegularFile:
                    return new RegularFileFso(path, settings);
                case FileSystemObjectType.DirectorySymlink:
                    return new DirectorySymlinkFso(path, settings);
                case FileSystemObjectType.Junction:
                    return new JunctionFso(path, settings);
                case FileSystemObjectType.FileSymlink:
                    return new FileSymlinkFso(path, settings);
                case FileSystemObjectType.FileReparsePoint:
                    return new FileReparsePointFso(path, settings);
                case FileSystemObjectType.FileHardlink:
                    return new FileHardlink(path, settings);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /*public static FileSystemObject Create(Guid fileSystemObjectId)
        {
            throw new NotImplementedException();
        }*/

        [JsonIgnore]
        public virtual bool IsDirectoryish
        {
            get { return false; }
        }

        [JsonIgnore]
        public bool IsLinkish
        {
            get
            {
                return Type == FileSystemObjectType.DirectorySymlink ||
                       Type == FileSystemObjectType.Junction ||
                       Type == FileSystemObjectType.FileSymlink ||
                       Type == FileSystemObjectType.FileHardlink;
            }
        }

        private IErrorReporter _reporter = null;
        private Func<FileSystemObject, BackupMode> _backupModeMap = null;
        private HashType _hashType = HashType.Md5;

        public abstract void Iterate(Action<FileSystemObject> f);

        protected IErrorReporter Reporter
        {
            get
            {
                var _this = this;
                while (_this.Parent != null)
                    _this = _this.Parent;
                return _this._reporter;
            }
        }

        protected Func<FileSystemObject, BackupMode> BackupModeMap
        {
            get
            {
                var _this = this;
                while (_this.Parent != null)
                    _this = _this.Parent;
                return _this._backupModeMap;
            }
        }

        protected bool ReportError(Exception e, string context = null)
        {
            AddException(e);
            var r = Reporter;
            return r == null || r.ReportError(e, context);
        }

        protected FileSystemObject()
        {
        }

        protected FileSystemObject(string path, FileSystemObjectSettings settings)
        {
            if (settings != null)
            {
                _reporter = settings.Reporter;
                _backupModeMap = settings.BackupModeMap;
            }

            string container;
            string name;
            path.SplitIntoObjectAndContainer(out container, out name);
            BasePath = container;
            Name = name;
            try
            {
                ModificationTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting file system object modification time for """ + path + @""""))
                    throw;
            }
        }

        protected void SetBackupMode()
        {
            if (BackupModeMap != null)
                BackupMode = BackupModeMap(this);
        }

        protected FileSystemObject(FileSystemObject parent, string name, string path = null)
        {
            Parent = parent;
            Name = name;
            path = path ?? Path;
            try
            {
                ModificationTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting file system object modification time for """ + path + @""""))
                    throw;
            }
        }

        public bool Contains(string path)
        {
            var myDecomposedPath = Path.DecomposePath().ToArray();
            var decomposedPath = path.DecomposePath().ToArray();
            if (!decomposedPath.PathStartsWith(myDecomposedPath))
                return false;
            if (decomposedPath.Length == myDecomposedPath.Length)
                return true;
            return Contains(decomposedPath, myDecomposedPath.Length - 1);
        }

        public bool Contains(string[] path, int start)
        {
            return Find(path, start) != null;
        }

        public FileSystemObject Find(string path)
        {
            var myDecomposedPath = Path.DecomposePath().ToArray();
            var decomposedPath = path.DecomposePath().ToArray();
            if (!decomposedPath.PathStartsWith(myDecomposedPath))
                return null;
            if (decomposedPath.Length == myDecomposedPath.Length)
                return this;
            return Find(decomposedPath, myDecomposedPath.Length - 1);
        }

        public abstract FileSystemObject Find(string[] path, int start);

        public Stream OpenForExclusiveRead()
        {
            return new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }

    public class SimpleReporter : IErrorReporter
    {
        public bool ReportError(Exception e, string context = null)
        {
            return true;
        }
    }
}
