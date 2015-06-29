using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Markup;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.FileSystem.FileSystemObjects.Exceptions;
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
        //parts of the file that changed, using the rsync algorithm. Only for
        //filish file system objects.
        Rsync,
    }

    public enum ChangeCriterium
    {
        ArchiveFlag,
        Size,
        Date,
        Hash,
        Custom,
    }

    public abstract class BaseBackupEngine
    {
        //Stream IDs are assigned per FileSystemObject, so a single path backed
        //up will have a different stream ID in every version it appears.
        public ulong NextStreamUniqueId = 1;
        //Differential-chain IDs are assigned per path per differential chain.
        //This is used to identify separate differential chains.
        public ulong NextDifferentialChainUniqueId = 1;

        private struct Range
        {
            public ulong Begin, End;
        }

        private readonly List<Range> _streamRanges = new List<Range>();
        private readonly List<Range> _differentialRanges = new List<Range>();

        //If followLinkTargets is set, the target locations of links that lead
        //to locations not covered by any source locations are added as source
        //locations.
        public abstract BackupMode GetBackupModeForObject(FileSystemObject o, out bool followLinkTargets);

        public virtual ChangeCriterium GetChangeCriterium(FileSystemObject newFile)
        {
            return newFile.Size < 1024*1024 ? ChangeCriterium.Hash : ChangeCriterium.Date;
        }

        private int _versionCount = -1;

        public int VersionCount
        {
            get
            {
                if (_versionCount >= 0)
                    return _versionCount;
                return _versionCount = (Versions.Count == 0 ? 0 : Versions.Back() + 1);
            }
        }

        public int NewVersionNumber 
        {
            get { return VersionCount; }
        }

        protected List<int> Versions = new List<int>();

        internal string GetVersionPath(int version)
        {
            return Path.Combine(TargetLocation, string.Format("version{0}.zip", version.ToString("00000000")));
        }

        protected string GetVersionDigestPath(int version)
        {
            return GetVersionPath(version) + ".sha256";
        }

        protected ZipFile OpenLatestVersion()
        {
            return new ZipFile(GetVersionPath(Versions.Back()));
        }

        private void SetVersions()
        {
            var regex = new Regex(@".*\\?version([0-9]+)\.zip$", RegexOptions.IgnoreCase);
            Versions.AddRange(Directory.EnumerateFiles(TargetLocation)
                .Select(x => regex.Match(x))
                .Where(x => x.Success)
                .Select(x => Convert.ToInt32(x.Groups[1].ToString())));
            Versions.Sort();
        }

        private static ZipEntry FindEntry(ZipFile zip, string name)
        {
            return zip.Entries.FirstOrDefault(entry => entry.FileName.PathMatch(name));
        }

        internal VersionManifest OpenVersionManifest(ZipFile zip, string versionPath)
        {
            var entry = FindEntry(zip, VersionManifestPath);
            if (entry == null)
                throw new InvalidBackup(TargetLocation, VersionManifestPath + " not found in " + versionPath);
            using (var stream = entry.OpenReader())
                return Serializer.Deserialize<VersionManifest>(stream);
        }

        internal FileSystemObject OpenBaseObject(ZipFile zip, int entryId)
        {
            var path = GetFileSystemObjectsDatPath(entryId);
            var entry = FindEntry(zip, path);
            if (entry == null)
                throw new InvalidBackup(TargetLocation, path + " not found");
            using (var stream = entry.OpenReader())
                return Serializer.Deserialize<FileSystemObject>(stream);
        }

        internal List<BackupStream> OpenBackupStream(ZipFile zip, int entryId)
        {
            var path = GetBackupStreamsDatPath(entryId);
            var entry = FindEntry(zip, path);
            if (entry == null)
                throw new InvalidBackup(TargetLocation, path + " not found");
            using (var stream = entry.OpenReader())
                return Serializer.Deserialize<List<BackupStream>>(stream);
        }

        protected BaseBackupEngine(string targetPath)
        {
            TargetLocation = targetPath;
            if (!Directory.Exists(targetPath))
            {
                if (File.Exists(targetPath))
                    throw new TargetLocationIsFile(targetPath);
                Directory.CreateDirectory(targetPath);
            }
            SetVersions();

            foreach (var version in Versions)
            {
                var versionPath = GetVersionPath(version);
                Range streamRange, diffRange;
                try
                {
                    var last = version == Versions.Back();
                    VersionManifest manifest;
                    using (var zip = new ZipFile(versionPath))
                        manifest = OpenVersionManifest(zip, versionPath);
                    if (manifest.VersionNumber != version)
                        throw new InvalidBackup(TargetLocation, "version " + version + " has the manifest of version " + manifest.VersionNumber);
                    streamRange.Begin = manifest.FirstStreamUniqueId;
                    streamRange.End = manifest.NextStreamUniqueId;
                    diffRange.Begin = manifest.FirstDifferentialChainUniqueId;
                    diffRange.End = manifest.NextDifferentialChainUniqueId;
                    if (!manifest.VersionDependencies.All(x => Versions.Contains(x)))
                        throw new InvalidBackup(TargetLocation, "version " + version + " depends on missing version(s)");
                    if (last)
                    {
                        NextStreamUniqueId = streamRange.End;
                        NextDifferentialChainUniqueId = diffRange.End;
                    }
                }
                catch (InvalidBackup)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new InvalidBackup(TargetLocation, e.Message);
                }
                _streamRanges.Add(streamRange);
                _differentialRanges.Add(diffRange);
            }
        }

        public ulong GetStreamId()
        {
            return NextStreamUniqueId++;
        }

        public abstract IEnumerable<string> GetSourceLocations();

        public string TargetLocation { get; private set; }

        public abstract IErrorReporter ErrorReporter { get; }

        public abstract HashType HashAlgorithm { get; }

        public virtual int CompressionLevelForFile(FileSystemObject fso)
        {
            return (int)CompressionLevel.Default;
        }

        public virtual int CompressionLevelForStructuralFiles
        {
            get { return (int)CompressionLevel.BestSpeed; }
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
            if (VersionCount == 0)
                CreateInitialVersion(startTime);
            else
                UpdateExistingVersion(startTime);
        }

        protected List<FileSystemObject> BaseObjects = new List<FileSystemObject>();

        private Func<FileSystemObject, BackupMode> MakeMap(List<string> forLaterCheck)
        {
            return x =>
            {
                bool follow;
                var ret = GetBackupModeForObject(x, out follow);
                if (follow && !string.IsNullOrEmpty(x.Target))
                {
                    throw new NotImplementedException("followLinkTargets not yet supported.");
                    //forLaterCheck.Add(x.Target);
                }
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

        private void CreateInitialVersion(DateTime startTime)
        {
            SetBaseObjects();
            ComputeAllHashes();
            GenerateFirstZip(startTime);
        }

        private void UpdateExistingVersion(DateTime startTime)
        {
            ulong firstDiff, firstStream;
            using (var zip = OpenLatestVersion())
            {
                var manifest = OpenVersionManifest(zip, string.Empty);
                for (int i = 0; i < manifest.EntryCount; i++)
                    _oldObjects.Add(OpenBaseObject(zip, i));
                firstDiff = manifest.NextDifferentialChainUniqueId;
                firstStream = manifest.NextStreamUniqueId;
            }
            SetBaseObjects();
            GenerateZip(startTime, CheckAndMaybeAdd, NewVersionNumber, firstStream, firstDiff);
        }

        public void RestoreBackup()
        {
            VersionForRestore latestVersion = null;
            try
            {
                {
                    var versions = new Dictionary<int, VersionForRestore>();
                    var stack = new Stack<int>();
                    var latestVersionNumber = Versions.Back();
                    stack.Push(latestVersionNumber);
                    while (stack.Count > 0)
                    {
                        var versionNumber = stack.Pop();
                        if (versions.ContainsKey(versionNumber))
                            continue;
                        var version = versions[versionNumber] = new VersionForRestore(versionNumber, this);
                        if (versionNumber == latestVersionNumber)
                            _oldObjects.AddRange(version.BaseObjects);
                        version.Manifest.VersionDependencies.ForEach(stack.Push);
                    }

                    latestVersion = versions[latestVersionNumber];
                    latestVersion.FillDependencies(versions);
                }

                foreach (var fileSystemObject in _oldObjects.Reversed())
                {
                    fileSystemObject.Iterate(x => Restore(x, latestVersion));
                }

            }
            finally
            {
                if (latestVersion != null)
                    latestVersion.Dispose();
            }
        }

        private void Restore(FileSystemObject fso, VersionForRestore version)
        {
            version.RestorePath(fso.Path);
        }

        FullStream GenerateInitialStream(FileSystemObject fso, HashSet<Guid> knownGuids)
        {
            if (!ShouldBeAdded(fso, knownGuids))
                return null;
            if (fso.FileSystemGuid != null)
                knownGuids.Add(fso.FileSystemGuid.Value);
            var ret = new FullStream
            {
                UniqueId = fso.StreamUniqueId,
                _physicalSize = fso.Size,
                _virtualSize = fso.Size,
            };
            ret.FileSystemObjects.Add(fso);
            return ret;
        }

        private void GenerateZip(DateTime startTime, Func<FileSystemObject, HashSet<Guid>, BackupStream> streamGenerator, int versionNumber = 0, ulong firstStreamId = 1, ulong firstDiffId = 1)
        {
            var zipPath = GetVersionPath(versionNumber);
            using (var zip = new ZipFile(zipPath))
            {
                zip.UseZip64WhenSaving = Zip64Option.AsNecessary;

                var streamDict = GenerateStreams(streamGenerator);
                var versionDependencies = new HashSet<int>();

                foreach (var kv in streamDict)
                {
                    var fso = BaseObjects[kv.Key];
                    kv.Value.ForEach(x => x.FileSystemObjects.ForEach(y => y.BackupStream = x));
                    fso.Iterate(x => GetDependencies(x, versionDependencies));
                    //Add FileSystemObjects.dat
                    {
                        var entry = zip.AddEntry(GetFileSystemObjectsDatPath(kv.Key),
                            x => Serializer.SerializeToStream(fso),
                            (x, y) => { if (y != null) y.Close(); });
                        entry.CompressionLevel = (CompressionLevel) CompressionLevelForStructuralFiles;
                    }
                    //Add BackupStreams.dat
                    {
                        var entry = zip.AddEntry(GetBackupStreamsDatPath(kv.Key),
                            x => Serializer.SerializeToStream(Streams),
                            (x, y) => { if (y != null) y.Close(); });
                        entry.CompressionLevel = (CompressionLevel) CompressionLevelForStructuralFiles;
                    }

                    var root = GetEntryRoot(kv.Key);
                    kv.Value.ForEach(x => AddToZip(zip, root, x.FileSystemObjects[0]));
                }

                AddVersionManifest(startTime, zip, versionDependencies, versionNumber, firstStreamId, firstDiffId);

                zip.Save();
            }

            var hashPath = GetVersionDigestPath(versionNumber);

            Console.WriteLine("Hashing result .zip...");

            byte[] digest;
            using (var file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                digest = SHA256.Create().ComputeHash(file);
            }

            using (var file = new StreamWriter(hashPath))
            {
                file.Write(Convert.ToBase64String(digest));
            }
        }

        private void GetDependencies(FileSystemObject fileSystemObject, HashSet<int> versionDependencies)
        {
            if (fileSystemObject.LatestVersion < 0)
                return;

            if (fileSystemObject.BackupStream == null)
            {
                if (!versionDependencies.Contains(fileSystemObject.LatestVersion))
                    versionDependencies.Add(fileSystemObject.LatestVersion);
            }
            else
            {
                fileSystemObject.BackupStream.GetDependencies(versionDependencies);
            }
        }

        private void GenerateFirstZip(DateTime startTime)
        {
            GenerateZip(startTime, GenerateInitialStream);
        }

        private Dictionary<int, List<BackupStream>> GenerateStreams(Func<FileSystemObject, HashSet<Guid>, BackupStream> streamGenerator)
        {
            var knownGuids = new HashSet<Guid>();
            var streamDict = new Dictionary<int, List<BackupStream>>();
            for (int i = 0; i < BaseObjects.Count; i++)
            {
                var fso = BaseObjects[i];
                var streams = new List<BackupStream>();
                fso.Iterate(x =>
                {
                    var stream = streamGenerator(x, knownGuids);
                    if (stream == null)
                        return;
                    x.SetUniqueIds(this);
                    stream.UniqueId = x.StreamUniqueId;
                    streams.Add(stream);
                    Streams.Add(stream);
                });
                streamDict[i] = streams;
            }
            return streamDict;
        }

        private void AddVersionManifest(DateTime startTime, ZipFile zip, IEnumerable<int> versionDependencies, int versionNumber = 0, ulong firstStreamId = 1, ulong firstDiffId = 1)
        {
            var entry = zip.AddEntry(VersionManifestPath, Serializer.SerializeToStream(new VersionManifest
            {
                CreationTime = startTime,
                VersionNumber = versionNumber,
                EntryCount = BaseObjects.Count,
                FirstStreamUniqueId = firstStreamId,
                FirstDifferentialChainUniqueId = firstDiffId,
                NextStreamUniqueId = NextStreamUniqueId,
                NextDifferentialChainUniqueId = NextDifferentialChainUniqueId,
                VersionDependencies = versionDependencies.Sorted().ToList(),
            }));
            entry.CompressionLevel = (CompressionLevel)CompressionLevelForStructuralFiles;
        }

        protected bool ShouldBeAdded(FileSystemObject fso, HashSet<Guid> knownGuids)
        {
            fso.LatestVersion = -1;
            if (fso.IsDirectoryish)
                return false;
            if (fso.BackupMode == BackupMode.NoBackup)
                return false;
            if (fso.IsLinkish && fso.Type != FileSystemObjectType.FileHardlink)
                return false;
            fso.LatestVersion = NewVersionNumber;
            if (fso.FileSystemGuid != null && knownGuids.Contains(fso.FileSystemGuid.Value))
                return false;
            return true;
        }

        private void AddToZip(ZipFile zip, string root, FileSystemObject fso)
        {
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

        private string GetEntryRoot(int entryId)
        {
            return string.Format(@"entries\{0}\", entryId.ToString("00000000"));
        }

        private const string VersionManifestPath = "manifest.dat";

        private string GetFileSystemObjectsDatPath(int entryId)
        {
            return GetEntryRoot(entryId) + "FileSystemObjects.dat";
        }

        internal string GetBackupStreamsDatPath(int entryId)
        {
            return GetEntryRoot(entryId) + "BackupStreams.dat";
        }

        private void ComputeAllHashes()
        {
            Console.WriteLine("Hashing input files...");
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
                            if (x.FileSystemGuid != null)
                            {
                                byte[] precomputed;
                                if (knownGuids.TryGetValue(new Tuple<Guid, HashType>(x.FileSystemGuid.Value, HashAlgorithm),
                                    out precomputed))
                                    x.Hashes[HashAlgorithm] = precomputed;
                                else
                                    knownGuids[new Tuple<Guid, HashType>(x.FileSystemGuid.Value, HashAlgorithm)] =
                                        x.ComputeHash(HashAlgorithm);
                            }
                            break;
                    }
                });
        }

        private bool _baseObjectsSet = false;
        private readonly List<FileSystemObject> _oldObjects = new List<FileSystemObject>();

        private IEnumerable<string> GetCurrentSourceLocations()
        {
            return VersionCount == 0 ? GetSourceLocations() : _oldObjects.Where(x => x.IsMain).Select(x => x.Path);
        }

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
            BaseObjects.AddRange(GetCurrentSourceLocations().Select(x => FileSystemObject.Create(x, settings)));
            BaseObjects.ForEach(x => x.IsMain = true);
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

        protected virtual bool FileHasChanged(FileSystemObject newFile, FileSystemObject oldFile)
        {
            return true;
        }

        private bool CompareHashes(FileSystemObject newFile, FileSystemObject oldFile)
        {
            if (oldFile.Hashes.Count == 0)
                return true;
            var kv = oldFile.Hashes.First();
            var newHash = newFile.ComputeHash(kv.Key);
            return !newHash.SequenceEqual(kv.Value);
        }

        private static FileSystemObject FindPath(IEnumerable<FileSystemObject> fso, string path)
        {
            return fso.Select(x => x.Find(path)).FirstOrDefault(x => x != null);
        }

        private bool FileHasChanged(FileSystemObject newFile)
        {
            var oldFile = FindPath(_oldObjects, newFile.Path);
            if (oldFile == null)
            {
                newFile.ComputeHash(HashAlgorithm);
                return true;
            }
            var crit = GetChangeCriterium(newFile);
            bool ret;
            switch (crit)
            {
                case ChangeCriterium.ArchiveFlag:
                    ret = newFile.ArchiveFlag;
                    break;
                case ChangeCriterium.Size:
                    ret = newFile.Size != oldFile.Size;
                    break;
                case ChangeCriterium.Date:
                    ret = newFile.ModificationTime != oldFile.ModificationTime;
                    break;
                case ChangeCriterium.Hash:
                    ret = CompareHashes(newFile, oldFile);
                    break;
                case ChangeCriterium.Custom:
                    ret = FileHasChanged(newFile, oldFile);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            if (!ret)
            {
                oldFile.Hashes.ForEach(x => newFile.Hashes[x.Key] = x.Value);
                newFile.ComputeHash(HashAlgorithm);
                newFile.LatestVersion = oldFile.LatestVersion;
            }
            return ret;
        }

        private BackupStream CheckAndMaybeAdd(FileSystemObject fso, HashSet<Guid> knownGuids)
        {
            if (!ShouldBeAdded(fso, knownGuids))
                return null;
            if (fso.BackupMode != BackupMode.ForceFull && !FileHasChanged(fso))
                return null;
            if (fso.FileSystemGuid != null)
                knownGuids.Add(fso.FileSystemGuid.Value);
            BackupStream newStream;
            switch (fso.BackupMode)
            {
                case BackupMode.ForceFull:
                case BackupMode.Full:
                    newStream = new FullStream
                        {
                            UniqueId = fso.StreamUniqueId,
                            _physicalSize = fso.Size,
                            _virtualSize = fso.Size,
                        };
                    newStream.FileSystemObjects.Add(fso);
                    break;
                case BackupMode.Rsync:
                    throw new NotImplementedException("Differential backup not implemented.");
                    //break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return newStream;
        }

        protected readonly List<BackupStream> Streams = new List<BackupStream>();

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

    }
}
