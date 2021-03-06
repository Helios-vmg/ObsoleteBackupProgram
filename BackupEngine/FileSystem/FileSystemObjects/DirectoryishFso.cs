﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Util;
using ProtoBuf;
using Directory = Alphaleonis.Win32.Filesystem.Directory;

namespace BackupEngine.FileSystem.FileSystemObjects
{
    [ProtoContract]
    [ProtoInclude(1003, typeof(DirectoryFso))]
    [ProtoInclude(1004, typeof(DirectorySymlinkFso))]
    public abstract class DirectoryishFso : FileSystemObject
    {

        private string[] GetEntries(string path)
        {
            try
            {
                return Alphaleonis.Win32.Filesystem.Directory.EnumerateFileSystemEntries(path).ToArray();
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

        protected DirectoryishFso(string path, string unmappedPath, FileSystemObjectSettings settings)
            : base(path, unmappedPath, settings)
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

        public override void DeleteExisting(string basePath = null)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            if (!Directory.Exists(path))
                return;
            Directory.Delete(path, true);
        }

        public FileSystemObject CreateChild(string name, string path = null)
        {
            path = path ?? (MappedPath + @"\" + name);
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
                    ret = new FileHardlinkFso(this, name, path);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return ret;
        }
    }

    [ProtoContract]
    public class DirectoryFso : DirectoryishFso
    {
        [ProtoMember(12, AsReference = true)]
        public List<FileSystemObject> Children;

        public DirectoryFso()
        {
        }

        public DirectoryFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
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
                Children = ConstructChildrenList(path ?? MappedPath);
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
            if (!path[start].PathMatch(Name))
                return null;
            if (start == path.Length - 1)
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

        public override void SetUniqueIds(BaseBackupEngine backup)
        {
            base.SetUniqueIds(backup);
            if (Children == null)
                return;
            foreach (var fileSystemObject in Children)
                fileSystemObject.SetUniqueIds(backup);
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            Directory.CreateDirectory(path);
        }
    }

    [ProtoContract]
    [ProtoInclude(1005, typeof(JunctionFso))]
    public class DirectorySymlinkFso : DirectoryishFso
    {
        public DirectorySymlinkFso()
        {
        }

        public DirectorySymlinkFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
        {
            SetTarget(path);
            SetBackupMode();
        }

        public DirectorySymlinkFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetTarget(path ?? MappedPath);
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

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            FileSystemOperations.CreateDirectorySymlink(path, Target);
        }
    }

    [ProtoContract]
    public class JunctionFso : DirectorySymlinkFso
    {
        public JunctionFso()
        {
        }

        public JunctionFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings: settings)
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

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            FileSystemOperations.CreateDirectoryJunction(path, Target);
        }
    }
}
