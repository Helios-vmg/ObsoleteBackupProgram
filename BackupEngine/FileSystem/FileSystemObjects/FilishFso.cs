using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
using BackupEngine.Util;
using Ionic.Crc;
using ProtoBuf;

namespace BackupEngine.FileSystem.FileSystemObjects
{
    [ProtoContract]
    [ProtoInclude(1006, typeof(RegularFileFso))]
    [ProtoInclude(1007, typeof(FileSymlinkFso))]
    [ProtoInclude(1008, typeof(FileHardlink))]
    public abstract class FilishFso : FileSystemObject
    {
        protected FilishFso()
        {
        }

        protected FilishFso(string path, FileSystemObjectSettings settings)
            : base(path, settings)
        {
            SetMembers(path);
        }

        protected FilishFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            SetMembers(path ?? Path);
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
            byte[] digest = null;
            if (Hashes.TryGetValue(type, out digest))
                return digest;
            try
            {
                using (var file = Alphaleonis.Win32.Filesystem.File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    switch (type)
                    {
                        case HashType.Sha1:
                            digest = ComputeSha1(file);
                            break;
                        case HashType.Md5:
                            digest = ComputeMd5(file);
                            break;
                        case HashType.Sha256:
                            digest = ComputeSha256(file);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("type");
                    }
                }
                if (digest != null)
                {
                    Hashes[type] = digest;
                }
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"computing hash for """ + Path + @""""))
                    throw;
            }
            return digest;
        }

        private byte[] ComputeSha1(FileStream file)
        {
            return SHA1.Create().ComputeHash(file);
        }

        private byte[] ComputeMd5(FileStream file)
        {
            return MD5.Create().ComputeHash(file);
        }

        private byte[] ComputeSha256(FileStream file)
        {
            return SHA256.Create().ComputeHash(file);
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
            var path = PathOverrideBaseWeak(basePath);
            if (!Alphaleonis.Win32.Filesystem.File.Exists(path))
                return;
            Alphaleonis.Win32.Filesystem.File.Delete(path);
        }
    }

    [ProtoContract]
    public class RegularFileFso : FilishFso
    {
        public RegularFileFso()
        {
        }

        public RegularFileFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
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
            var path = PathOverrideBaseWeak(basePath);
            using (var file = Alphaleonis.Win32.Filesystem.File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                stream.CopyTo(file);
        }
    }

    [ProtoContract]
    [ProtoInclude(1009, typeof(FileReparsePointFso))]
    public class FileSymlinkFso : FilishFso
    {
        public FileSymlinkFso() : base()
        {
            
        }

        public FileSymlinkFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
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
            SetMembers(path ?? Path);
            SetBackupMode();
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileSymlink; }
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideBaseWeak(basePath);
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

        public FileReparsePointFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
            throw new ReparsePointsNotImplemented(path);
        }

        public FileReparsePointFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            throw new ReparsePointsNotImplemented(path ?? Path);
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileReparsePoint; }
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideBaseWeak(basePath);
            FileSystemOperations.CreateFileReparsePoint(path, Target);
        }
    }

    [ProtoContract]
    public class FileHardlink : FilishFso
    {
        public readonly List<string> Peers;
        public FileHardlink()
        {
        }

        public FileHardlink(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
            Peers = FileSystemOperations.ListAllHardlinks(path);
            SetBackupMode();
        }

        public FileHardlink(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
            Peers = FileSystemOperations.ListAllHardlinks(path);
            SetBackupMode();
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileHardlink; }
        }

        protected override void RestoreInternal(string basePath)
        {
            var path = PathOverrideBaseWeak(basePath);
            FileSystemOperations.CreateHardlink(path, Target);
        }
    }
}
