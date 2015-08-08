using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.Util;
using ProtoBuf;

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

    public class FileSystemObjectSettings
    {
        public readonly BaseBackupEngine BackupEngine = null;
        public IErrorReporter Reporter = null;
        public Func<FileSystemObject, BackupMode> BackupModeMap = null;

        public FileSystemObjectSettings(BaseBackupEngine bbe)
        {
            BackupEngine = bbe;
        }
    }

    [ProtoContract]
    [ProtoInclude(1001, typeof(DirectoryishFso))]
    [ProtoInclude(1002, typeof(FilishFso))]
    public abstract class FileSystemObject
    {
        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path) ?? string.Empty;
        }

        [ProtoMember(1)] public ulong StreamUniqueId;
        [ProtoMember(2)] public ulong DifferentialChainUniqueId;
        [ProtoMember(3)] public string Name;
        [ProtoMember(4)] public string BasePath;
        [ProtoMember(5)] public string Target;
        [ProtoMember(6)] public long Size;
        [ProtoMember(7)] public DateTime ModificationTime;
        [ProtoMember(8)] public Guid? FileSystemGuid;
        [ProtoMember(9)] public Dictionary<HashType, byte[]> Hashes = new Dictionary<HashType, byte[]>();
        [ProtoMember(10)] public List<string> Exceptions = new List<string>();
        [ProtoMember(11)] public bool IsMain;
        [ProtoMember(13, AsReference = true)] public FileSystemObject Parent;
        [ProtoMember(14)] public int LatestVersion;

        public abstract FileSystemObjectType Type { get; }

        public string Path
        {
            get { return PathOverrideBase(null, false); }
        }

        public string PathWithoutBase
        {
            get { return PathOverrideBase(); }
        }

        public int EntryNumber = -1;

        private int GetEntryContainer
        {
            get
            {
                for (var _this = this; _this != null; _this = _this.Parent)
                    if (_this.EntryNumber >= 0)
                        return _this.EntryNumber;
                return -1;
            }
        }

        public string ZipPath
        {
            get
            {
                var noBase = PathWithoutBase;
                var container = GetEntryContainer;
                Debug.Assert(container >= 0);
                return BaseBackupEngine.GetEntryRoot(container) + noBase;
            }
        }

        protected string PathOverrideBaseWeak(string basePath = null)
        {
            return PathOverrideBase(basePath, basePath != null);
        }

        protected string PathOverrideBase(string basePath = null, bool @override = true)
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

            var s = !@override ? _this.BasePath : basePath.NormalizePath();
            if (s != null)
            {
                temp.Add(s);
                expectedLength += s.Length + 1;
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

        public virtual long RecursiveSize
        {
            get
            {
                if (BackupMode == BackupMode.NoBackup)
                    return 0;
                return Size;
            }
        }

        public bool ArchiveFlag { get; protected set; }
        

        //Does not compute the hash if it hasn't been computed already.
        public virtual byte[] GetHash(HashType type)
        {
            return Hashes.GetValueOrDefault(type, null);
        }

        //Computes the hash if necessary.
        public abstract byte[] ComputeHash(HashType type);

        public void ComputeAnyHash()
        {
            ComputeHash(HashType.Default);
        }

        /*public static FileSystemObjectType GetType(Guid fileSystemObjectId)
        {
            throw new NotImplementedException();
        }*/

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

        public virtual bool IsDirectoryish
        {
            get { return false; }
        }

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

        private BaseBackupEngine _backupEngine;

        protected BaseBackupEngine BackupEngine
        {
            get { return _backupEngine ?? (Parent != null ? Parent.BackupEngine : null); }
        }
        private IErrorReporter _reporter;
        private Func<FileSystemObject, BackupMode> _backupModeMap;

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

        public virtual bool StreamRequired
        {
            get { return false; }
        }

        public BackupStream BackupStream;

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
                _backupEngine = settings.BackupEngine;
                _reporter = settings.Reporter;
                _backupModeMap = settings.BackupModeMap;
            }

            string container;
            string name;
            path.SplitIntoObjectAndContainer(out container, out name);
            BasePath = container;
            Name = name;
            SetFileAttributes(path);
        }

        private void SetFileAttributes(string path)
        {
            try
            {
                ArchiveFlag = (Alphaleonis.Win32.Filesystem.File.GetAttributes(path) & FileAttributes.Archive) == FileAttributes.Archive;
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting file system object attributes for """ + path + @""""))
                    throw;
                ArchiveFlag = true;
            }
            try
            {
                ModificationTime = Alphaleonis.Win32.Filesystem.File.GetLastWriteTimeUtc(path);
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
            SetFileAttributes(path);
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
            return Alphaleonis.Win32.Filesystem.File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        public virtual void SetUniqueIds(BaseBackupEngine backup)
        {
            StreamUniqueId = backup.GetStreamId();
        }

        public abstract void DeleteExisting(string basePath = null);

        public virtual bool Restore(string basePath = null)
        {
            if (StreamRequired)
                return false;
            RestoreInternal(basePath);
            return true;
        }

        protected virtual void RestoreInternal(string basePath)
        {
            throw new Exception("Incorrect implementation.");
        }

        public virtual void Restore(Stream stream, string basePath = null)
        {
            throw new Exception("Incorrect implementation.");
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
