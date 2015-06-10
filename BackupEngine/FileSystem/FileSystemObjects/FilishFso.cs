using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.Util;
using Ionic.Crc;
using Newtonsoft.Json;

namespace BackupEngine.FileSystem.FileSystemObjects
{
    [JsonObject(MemberSerialization.OptOut)]
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

            try
            {
                UniqueId = FileSystemOperations.GetFileGuid(path);
            }
            catch (Exception e)
            {
                if (!ReportError(e, @"getting unique ID for """ + path + @""""))
                    throw;
                UniqueId = null;
            }
        }

        public override byte[] ComputeHash(HashType type)
        {
            byte[] digest = null;
            if (Hashes.TryGetValue(type, out digest))
                return digest;
            try
            {
                using (var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    switch (type)
                    {
                        case HashType.Crc32:
                            digest = ComputeCrc32(file);
                            break;
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

        private byte[] ComputeCrc32(FileStream file)
        {
            var crc32 = new CRC32().GetCrc32(file);
            return BitConverter.GetBytes(crc32);
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
    }

    [JsonObject(MemberSerialization.OptOut)]
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
    }

    [JsonObject(MemberSerialization.OptOut)]
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
    }

    [JsonObject(MemberSerialization.OptOut)]
    public class FileReparsePointFso : FileSymlinkFso
    {
        public FileReparsePointFso()
        {
        }

        public FileReparsePointFso(string path, FileSystemObjectSettings settings = null)
            : base(path, settings)
        {
        }

        public FileReparsePointFso(FileSystemObject parent, string name, string path = null)
            : base(parent, name, path)
        {
        }

        public override FileSystemObjectType Type
        {
            get { return FileSystemObjectType.FileReparsePoint; }
        }
    }

    [JsonObject(MemberSerialization.OptOut)]
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
    }
}
