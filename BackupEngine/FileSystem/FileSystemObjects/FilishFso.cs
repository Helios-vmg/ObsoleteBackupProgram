using System;
using System.Collections.Generic;
using System.IO;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Util;
using ProtoBuf;
using File = Alphaleonis.Win32.Filesystem.File;

namespace BackupEngine.FileSystem.FileSystemObjects
{
    [ProtoContract]
    [ProtoInclude(1006, typeof(RegularFileFso))]
    [ProtoInclude(1007, typeof(FileSymlinkFso))]
    public abstract class FilishFso : FileSystemObject
    {
        protected FilishFso()
        {
        }

        protected FilishFso(string path, string unmappedPath, FileSystemObjectSettings settings)
            : base(path, unmappedPath, settings)
        {
            SetMembers(path);
        }

        protected FilishFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetMembers(path ?? MappedPath);
        }

        private void SetMembers(string path)
        {
            try
            {
                Size = FileSystemOperations.GetFileSize(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting file size for """ + path + @""""))
                    throw;
                Size = 0;
            }

            SetFileSystemGuid(path);
        }

        public void SetFileSystemGuid(string path, bool retry = true)
        {
            try
            {
                FileSystemGuid = FileSystemOperations.GetFileGuid(path);
            }
            catch (UnableToObtainGuid e)
            {
                if (retry)
                    BackupEngine.EnqueueFileForGuidGet(this);
                else
                {
                    if (!ReportError(e, @"getting unique ID for """ + path + @""""))
                        throw;
                    FileSystemGuid = null;
                }
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting unique ID for """ + path + @""""))
                    throw;
                FileSystemGuid = null;
            }
        }

        public override byte[] ComputeHash(HashType type)
        {
            byte[] digest;
            if (Hashes.TryGetValue(type, out digest))
                return digest;
            try
            {
                using (var file = Alphaleonis.Win32.Filesystem.File.Open(MappedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Hashes[type] = digest = Hash.New(type).ComputeHash(file);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"computing hash for """ + MappedPath + @""""))
                    throw;
            }
            return digest;
        }

        public override void Iterate(Action<FileSystemObject> f)
        {
            f(this);
        }

        public override FileSystemObject Find(string[] path, int start)
        {
            return start == path.Length - 1 && path[start].PathMatch(Name) ? this : null;
        }

        public override void DeleteExisting(string basePath = null)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            if (!File.Exists(path))
                return;
            File.Delete(path);
        }
    }

    [ProtoContract]
    [ProtoInclude(1008, typeof(FileHardlinkFso))]
    public class RegularFileFso : FilishFso
    {
        public RegularFileFso()
        {
        }

        public RegularFileFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
        {
            SetBackupMode();
        }

        public RegularFileFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetBackupMode();
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.RegularFile; }
        }

        public override bool StreamRequired
        {
            get { return true; }
        }

        public override void Restore(Stream stream, string basePath = null)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            using (var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                stream.CopyTo(file);
        }
    }

    [ProtoContract]
    [ProtoInclude(1009, typeof(FileReparsePointFso))]
    public class FileSymlinkFso : FilishFso
    {
        public FileSymlinkFso()
        {
            
        }

        public FileSymlinkFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
        {
            SetMembers(path);
            SetBackupMode();
        }

        private void SetMembers(string path)
        {
            try
            {
                Target = FileSystemOperations.GetReparsePointTarget(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting target for """ + path + @""""))
                    throw;
            }
        }

        public FileSymlinkFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetMembers(path ?? MappedPath);
            SetBackupMode();
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileSymlink; }
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            FileSystemOperations.CreateSymlink(path, Target);
        }
    }

    [ProtoContract]
    public class FileReparsePointFso : FileSymlinkFso
    {
        public FileReparsePointFso()
        {
            throw new ReparsePointsNotImplemented(string.Empty);
        }

        public FileReparsePointFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
        {
            throw new ReparsePointsNotImplemented(path);
        }

        public FileReparsePointFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            throw new ReparsePointsNotImplemented(path ?? MappedPath);
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileReparsePoint; }
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideUnmappedBaseWeak(basePath);
            FileSystemOperations.CreateFileReparsePoint(path, Target);
        }
    }

    [ProtoContract]
    public class FileHardlinkFso : RegularFileFso
    {
        [ProtoMember(16)] public List<string> Peers;
        public bool TreatAsFile;

        public FileHardlinkFso()
        {
        }

        public FileHardlinkFso(string path, string unmappedPath, FileSystemObjectSettings settings = null)
            : base(path, unmappedPath, settings)
        {
            Peers = FileSystemOperations.ListAllHardlinks(path);
            SetBackupMode();
        }

        public FileHardlinkFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            Peers = FileSystemOperations.ListAllHardlinks(path);
            SetBackupMode();
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileHardlink; }
        }

        public override bool StreamRequired
        {
            get { return true; }
        }

        public override bool Restore(string basePath = null)
        {
            if (Target == null)
                return false;
            var path = PathOverrideUnmappedBaseWeak(basePath);
            FileSystemOperations.CreateHardlink(path, Target);
            return true;
        }
    }
}
