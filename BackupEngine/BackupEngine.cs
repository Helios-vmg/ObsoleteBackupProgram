using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.Serialization;
using BackupEngine.Util;
using Ionic.Zip;
using Ionic.Zlib;

namespace BackupEngine
{
    public enum BackupMode
    {
        //File or directory will not be backed up.
        NoBackup,
        //Directory will be backed up. Only for directoryish file system objects.
        //Link-like objects will be backed up as links.
        Directory,
        //File will be backed up unconditionally. Only for filish file system
        //objects.
        ForceFull,
        //File will be backed up fully if changes were made to it. Only for
        //filish file system objects.
        Full,
        //File will be backed up if changes were made to it, storing only the
        //parts of the file that changed. Only for filish file system objects.
        Differential,
    }

    public class InvalidBackupMode : Exception
    {
        public InvalidBackupMode(string message) : base(message)
        {
        }
    }

    public class TargetLocationIsFile : Exception
    {
        public TargetLocationIsFile(string path)
            : base("Target location \"" + path + "\" is a file.")
        {
        }
    }

    public abstract class BaseBackupEngine
    {
        //If followLinkTargets is set, the target locations of links that lead
        //to locations not covered by any source locations are added as source
        //locations.
        public abstract BackupMode GetBackupModeForObject(FileSystemObject o, out bool followLinkTargets);

        public abstract IEnumerable<string> GetSourceLocations();

        public abstract string TargetLocation { get; }

        public abstract IErrorReporter ErrorReporter { get; }

        public abstract HashType HashAlgorithm { get; }

        public virtual int CompressionLevelForFile(FileSystemObject fso)
        {
            return (int)CompressionLevel.Default;
        }

        private CompressionLevel InternalCompressionLevelForFile(FileSystemObject fso)
        {
            var level = CompressionLevelForFile(fso);
            if (level < 0 || level > 9)
                return CompressionLevel.Default;
            return (CompressionLevel) level;
        }

        public void PerformBackup()
        {
            var startTime = DateTime.UtcNow;
            var targetPath = TargetLocation;
            if (!Directory.Exists(targetPath))
            {
                if (File.Exists(targetPath))
                    throw new TargetLocationIsFile(targetPath);
                Directory.CreateDirectory(targetPath);
                CreateInitialVersion(targetPath, startTime);
            }
            else
                UpdateExistingVersion(targetPath);
        }

        protected List<FileSystemObject> BaseObjects = new List<FileSystemObject>();

        private Func<FileSystemObject, BackupMode> MakeMap(List<string> forLaterCheck)
        {
            return x =>
            {
                bool follow;
                var ret = GetBackupModeForObject(x, out follow);
                if (follow && !string.IsNullOrEmpty(x.Target))
                    forLaterCheck.Add(x.Target);
                return ret;
            };
        }

        public List<FileSystemObject> AllObjects()
        {
            SetBaseObjects();
            var ret = new List<FileSystemObject>();
            BaseObjects.ForEach(x => x.Iterate(ret.Add));
            return ret;
        }

        private void CreateInitialVersion(string targetPath, DateTime startTime)
        {
            SetBaseObjects();
            ComputeAllHashes();
            GenerateFirstZip(targetPath, startTime);
        }

        private void Add(ZipFile zip, string root, FileSystemObject fso, HashSet<Guid> knownGuids)
        {
            if (fso.IsDirectoryish)
                return;
            if (fso.BackupMode == BackupMode.NoBackup)
                return;
            if (fso.IsLinkish && fso.Type != FileSystemObjectType.FileHardlink)
                return;
            if (fso.UniqueId != null)
            {
                if (knownGuids.Contains(fso.UniqueId.Value))
                    return;
                if (fso.UniqueId != null)
                    knownGuids.Add(fso.UniqueId.Value);
            }

            var filename = root + fso.PathWithoutBase;
            var entry = zip.AddEntry(
                filename,
                x =>
                {
                    Console.WriteLine(fso.Path);
                    return fso.OpenForExclusiveRead();
                },
                (x, y) =>
                {
                    if (y != null) y.Close();
                });
            entry.CompressionLevel = InternalCompressionLevelForFile(fso);
        }

        private void GenerateFirstZip(string targetPath, DateTime startTime)
        {
            using (var zip = new ZipFile(targetPath + @"\\version0.zip"))
            {
                zip.UseZip64WhenSaving = Zip64Option.AsNecessary;
                var entry = zip.AddEntry("manifest.json", Serializer.SerializeToStream(new VersionManifest
                {
                    CreationTime = startTime,
                    VersionNumber = 0,
                    EntryCount = BaseObjects.Count,
                }));
                entry.CompressionLevel = CompressionLevel.BestSpeed;

                var index = 0;
                var knownGuids = new HashSet<Guid>();
                for (int i = 0; i < BaseObjects.Count; i++)
                {
                    var fso = BaseObjects[i];
                    var root = "entry" + index++ + @"\";
                    entry = zip.AddEntry(root + "manifest.json", x => Serializer.SerializeToStream(fso),
                        (x, y) => { if (y != null) y.Close(); });
                    entry.CompressionLevel = CompressionLevel.BestSpeed;
                    fso.Iterate(x => Add(zip, root, x, knownGuids));
                }

                zip.Save();
            }
        }

        private void ComputeAllHashes()
        {
            var knownGuids = new Dictionary<Tuple<Guid, HashType>, byte[]>();
            foreach (var fso in BaseObjects)
                fso.Iterate(x =>
                {
                    switch (x.Type)
                    {
                        case FileSystemObjectType.RegularFile:
                            x.ComputeHash(HashAlgorithm);
                            break;
                        case FileSystemObjectType.FileHardlink:
                            if (x.UniqueId != null)
                            {
                                byte[] precomputed;
                                if (knownGuids.TryGetValue(new Tuple<Guid, HashType>(x.UniqueId.Value, HashAlgorithm),
                                    out precomputed))
                                    x.Hashes[HashAlgorithm] = precomputed;
                                else
                                    knownGuids[new Tuple<Guid, HashType>(x.UniqueId.Value, HashAlgorithm)] =
                                        x.ComputeHash(HashAlgorithm);
                            }
                            break;
                    }
                });
        }

        private bool _baseObjectsSet = false;

        private void SetBaseObjects()
        {
            if (_baseObjectsSet)
                return;
            _baseObjectsSet = true;
            var forLaterCheck = new List<string>();
            var settings = new FileSystemObjectSettings
            {
                Reporter = ErrorReporter,
                BackupModeMap = MakeMap(forLaterCheck),
            };
            BaseObjects.AddRange(GetSourceLocations().Select(x => FileSystemObject.Create(x, settings)));
            while (forLaterCheck.Count > 0)
            {
                var oldForLaterCheck = forLaterCheck;
                forLaterCheck = new List<string>();
                settings.BackupModeMap = MakeMap(forLaterCheck);
                foreach (var path in oldForLaterCheck)
                    if (!Covered(path))
                        BaseObjects.Add(FileSystemObject.Create(path, settings));
            }
        }

        private bool Covered(string path)
        {
            return BaseObjects.Any(x => x.Contains(path));
        }

        private void UpdateExistingVersion(string targetPath)
        {
            throw new NotImplementedException();
        }

        private static string[] Sizes =
        {
            " B",
            " KiB",
            " MiB",
            " GiB",
            " TiB",
            " PiB",
            " EiB",
        };

        public static string FormatSize(long size)
        {
            double f = size;
            var index = 0;
            for (; f >= 1024.0 && index < Sizes.Length - 1; index++)
                f /= 1024.0;
            return String.Format("{0:0.#}", f) + Sizes[index];
        }

        public void GenerateSizeReport(out List<DirectoryFso> largestDirectories, out List<FileSystemObject> largestFiles,
            out List<Tuple<string, long>> largestExtensions)
        {
            var list = AllObjects();
            largestDirectories = list
                .OfType<DirectoryFso>()
                .Where(x => x.RecursiveSize != 0)
                .OrderByDescending(x => x.RecursiveSize).ToList();

            var allFiles =
                list.Where(
                    x => x.Type == FileSystemObjectType.RegularFile || x.Type == FileSystemObjectType.FileHardlink)
                    .ToList();
            largestFiles = allFiles.OrderByDescending(x => x.RecursiveSize).ToList();

            var dict = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var fso in allFiles)
            {
                var ext = Path.GetExtension(fso.Name) ?? string.Empty;
                if (!dict.ContainsKey(ext))
                    dict[ext] = fso.RecursiveSize;
                else
                    dict[ext] += fso.RecursiveSize;
            }
            largestExtensions = dict
                .OrderByDescending(x => x.Value)
                .Select(x => new Tuple<string, long>(x.Key, x.Value))
                .ToList();
        }

        public static FileSystemObject TestRead(string path)
        {
            using (var file = new ZipFile(path))
            {
                var entry = file.Entries.FirstOrDefault(x => x.FileName == "entry0/manifest.json");
                if (entry == null)
                    return null;
                var buffer = new byte[entry.UncompressedSize];
                entry.OpenReader().Read(buffer, 0, buffer.Length);
                return Serializer.Deserialize(buffer);
            }
        }
    }
}
