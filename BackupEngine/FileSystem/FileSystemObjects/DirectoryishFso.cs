using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.Util;
using Newtonsoft.Json;

namespace BackupEngine.FileSystem.FileSystemObjects
{
    [JsonObject(MemberSerialization.OptOut)]
    public abstract class DirectoryishFso : FileSystemObject
    {

        private string[] GetEntries(string path)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(path).ToArray();
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting directory entries for """ + path + @""""))
                    throw;
                return null;
            }
        }

        protected DirectoryishFso()
        {
        }

        protected DirectoryishFso(string path, FileSystemObjectSettings settings)
            : base(path, settings)
        {
        }

        protected DirectoryishFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
        }

        protected List<FileSystemObject> ConstructChildrenList(string path)
        {
            var array = GetEntries(path);
            var ret = array == null ? new List<FileSystemObject>() : array.Select(x => CreateChild(x.GetNameFromPath(), x)).ToList();
            ret.Sort((x, y) => x.IsDirectoryish && !y.IsDirectoryish ? -1 : (y.IsDirectoryish && !x.IsDirectoryish ? 1 : 0));
            return ret;
        }

        public override long RecursiveSize
        {
            get
            {
                return 0;
            }
        }


        public override byte[] GetHash(HashType type)
        {
            return null;
        }

        public override byte[] ComputeHash(HashType type)
        {
            return null;
        }

        public override string ToString()
        {
            return "[" + base.ToString() + "]";
        }

        public override bool IsDirectoryish
        {
            get { return true; }
        }

        public FileSystemObject CreateChild(string name, string path = null)
        {
            path = path ?? (Path + @"\" + name);
            FileSystemObject ret;
            switch (GetType(path))
            {
                case FileSystemObjectType.Directory:
                    ret = new DirectoryFso(this, name, path);
                    break;
                case FileSystemObjectType.RegularFile:
                    ret = new RegularFileFso(this, name, path);
                    break;
                case FileSystemObjectType.DirectorySymlink:
                    ret = new DirectorySymlinkFso(this, name, path);
                    break;
                case FileSystemObjectType.Junction:
                    ret = new JunctionFso(this, name, path);
                    break;
                case FileSystemObjectType.FileSymlink:
                    ret = new FileSymlinkFso(this, name, path);
                    break;
                case FileSystemObjectType.FileReparsePoint:
                    ret = new FileReparsePointFso(this, name, path);
                    break;
                case FileSystemObjectType.FileHardlink:
                    ret = new FileHardlink(this, name, path);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return ret;
        }
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class DirectoryFso : DirectoryishFso
    {
        public List<FileSystemObject> Children;

        public DirectoryFso()
        {
        }

        public DirectoryFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
            SetBackupMode();
            if (BackupMode == BackupMode.Directory)
                Children = ConstructChildrenList(path);
        }

        public DirectoryFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetBackupMode();
            if (BackupMode == BackupMode.Directory)
                Children = ConstructChildrenList(path ?? Path);
        }

        public override long RecursiveSize
        {
            get
            {
                if (Children == null || BackupMode == BackupMode.NoBackup)
                    return 0;
                return Children.Sum(x => x.RecursiveSize);
            }
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.Directory; }
        }

        public override FileSystemObject Find(string[] path, int start)
        {
            if (start == path.Length - 1 && path[start].PathMatch(Name))
                return this;
            if (Children == null)
                return null;
            return Children
                .Select(x => x.Find(path, start + 1))
                .FirstOrDefault(x => x != null);
        }

        public override void Iterate(Action<FileSystemObject> f)
        {
            f(this);
            if (Children == null)
                return;
            foreach (var child in Children)
                child.Iterate(f);
        }
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class DirectorySymlinkFso : DirectoryishFso
    {
        public DirectorySymlinkFso()
        {
        }

        public DirectorySymlinkFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
            SetTarget(path);
            SetBackupMode();
        }

        public DirectorySymlinkFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetTarget(path ?? Path);
            SetBackupMode();
        }

        private void SetTarget(string path)
        {
            try
            {
                Target = FileSystemOperations.GetReparsePointTarget(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting link target for """ + path + @""""))
                    throw;
            }
            
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.DirectorySymlink; }
        }

        public override void Iterate(Action<FileSystemObject> f)
        {
            f(this);
        }

        public override FileSystemObject Find(string[] path, int start)
        {
            return start == path.Length - 1 && path[start].PathMatch(Name) ? this : null;
        }
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class JunctionFso : DirectorySymlinkFso
    {
        public JunctionFso()
        {
        }

        public JunctionFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
        }

        public JunctionFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.Junction; }
        }
    }
}
