using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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

    [Flags]
    public enum InstantiationPurpose
    {
        None = 0,
        PerformBackup = 1 << 0,
        RestoreBackup = 2 << 0,
        VerifyBackup = 3 << 0,
        TestBackup = 4 << 0,
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
        public const ulong InvalidStreamId = 0;
        public const ulong InvalidDifferentialChainId = 0;
        //Stream IDs are assigned per FileSystemObject, so a single path backed
        //up will have a different stream ID in every version it appears.
        public ulong NextStreamUniqueId = InvalidStreamId + 1;
        //Differential-chain IDs are assigned per path per differential chain.
        //This is used to identify separate differential chains.
        public ulong NextDifferentialChainUniqueId = InvalidDifferentialChainId + 1;

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

        public IEnumerable<int> GetVersions()
        {
            return Versions;
        }

        public bool VersionExists(int version)
        {
            return Versions.Contains(version);
        }

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

        public static ZipEntry FindEntry(ZipFile zip, string name)
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
            {
                var ret = Serializer.Deserialize<FileSystemObject>(stream);
                ret.EntryNumber = entryId;
                return ret;
            }
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

        public List<int> GetVersionDependencies(int version)
        {
            var versionPath = GetVersionPath(version);
            VersionManifest manifest;
            using (var zip = new ZipFile(versionPath))
                manifest = OpenVersionManifest(zip, versionPath);
            return manifest.VersionDependencies;
        }

        public IEnumerable<FileSystemObject> GetEntries(int version)
        {
            var versionPath = GetVersionPath(version);
            using (var zip = new ZipFile(versionPath))
            {
                var manifest = OpenVersionManifest(zip, versionPath);
                for (int i = 0; i < manifest.EntryCount; i++)
                    yield return OpenBaseObject(zip, i);
            }
        }

        protected BaseBackupEngine(string targetPath)
        {
            TargetLocation = targetPath;
            if (!Directory.Exists(targetPath))
            {
                if (Alphaleonis.Win32.Filesystem.File.Exists(targetPath))
                    throw new TargetLocationIsFile(targetPath);
                Directory.CreateDirectory(targetPath);
            }
            SetVersions();
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

        private SystemOperations.VolumeSnapshot _currentSnapshot;
        private Dictionary<string, SystemOperations.VolumeInfo> _currentVolumes;
        private List<Tuple<Regex, string>> _pathMapper;
        private List<Tuple<Regex, string>> _reversePathMapper;

        private void SetPathMapper()
        {
            _pathMapper = new List<Tuple<Regex, string>>();
            _reversePathMapper = new List<Tuple<Regex, string>>();
            Action<string, string, List<Tuple<Regex, string>>> f = (x, y, z) => z.Add(new Tuple<Regex, string>(new Regex(string.Format(@"({0})(\\.*|$)", Regex.Escape(x)), RegexOptions.IgnoreCase), y));
            foreach (var shadow in _currentSnapshot.Shadows)
            {
                var volume = _currentVolumes[shadow.OriginalVolumeName];
                var s = shadow.SnapshotDeviceObject.EnsureLastCharacterIsNotBackslash();
                foreach (var path in volume.MountedPaths)
                {
                    var s2 = path.EnsureLastCharacterIsNotBackslash();
                    f(s2, s, _pathMapper);
                    f(s, s2, _reversePathMapper);
                }
            }
        }

        private static string MapPath(string path, List<Tuple<Regex, string>> list)
        {
            if (list == null)
                return path;
            string ret = null;
            int longest = 0;
            foreach (var tuple in list)
            {
                var match = tuple.Item1.Match(path);
                if (!match.Success)
                    continue;
                var g1 = match.Groups[1].ToString();
                if (g1.Length <= longest)
                    continue;
                longest = g1.Length;
                var g2 = match.Groups[2].ToString();
                ret = tuple.Item2 + g2;
            }
            return ret ?? path;
        }

        private string MapPathForward(string path)
        {
            return MapPath(path, _pathMapper);
        }

        private string MapPathBack(string path)
        {
            return MapPath(path, _reversePathMapper);
        }

        private bool IsBackupable(DriveType type)
        {
            switch (type)
            {
                case DriveType.Unknown:
                case DriveType.NoRootDirectory:
                case DriveType.Network:
                case DriveType.CDRom:
                    return false;
                case DriveType.Removable:
                case DriveType.Fixed:
                case DriveType.Ram:
                    return true;
            }
            throw new ArgumentOutOfRangeException("type");
        }

        public bool UseSnapshots = true;

        public void PerformBackup()
        {
            var startTime = DateTime.UtcNow;
            _currentVolumes = SystemOperations.EnumerateVolumes().Where(x => IsBackupable(x.DriveType)).ToDictionary(x => x.VolumePath);
            if (!UseSnapshots)
            {
                PerformBackupInner(startTime);
                return;
            }
            using (_currentSnapshot = new SystemOperations.VolumeSnapshot(_currentVolumes.Keys))
            {
                foreach (var shadow in _currentSnapshot.Shadows)
                {
                    startTime = shadow.CreatedAt;
                    break;
                }
                SetPathMapper();
                PerformBackupInner(startTime);
            }
            _currentSnapshot = null;
        }

        private void PerformBackupInner(DateTime startTime)
        {
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

        private void SetOldObjectsDict()
        {
            foreach (var fso in _oldObjects)
            {
                fso.Iterate(x =>
                {
                    _oldObjectsDict[x.Path.SimplifyPath()] = x;
                });
            }
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
            SetOldObjectsDict();
            SetBaseObjects();
            foreach (var baseObject in BaseObjects)
            {
                baseObject.Iterate(fso =>
                {
                    FileSystemObject found;
                    if (!_oldObjectsDict.TryGetValue(fso.Path.SimplifyPath(), out found))
                        return;
                    fso.StreamUniqueId = found.StreamUniqueId;
                });
            }
            GenerateZip(startTime, CheckAndMaybeAdd, NewVersionNumber, firstStream, firstDiff);
        }

        public void RestoreBackup()
        {
            VersionForRestore latestVersion = null;
            try
            {
                Console.WriteLine("Initializing structures...");
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
                    //latestVersion.SetAllStreamIds();
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
            version.Restore(fso);
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
                PhysicalSize = fso.Size,
                VirtualSize = fso.Size,
                ZipPath = fso.ZipPath,
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
            using (var file = Alphaleonis.Win32.Filesystem.File.Open(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                digest = SHA256.Create().ComputeHash(file);
            }

            using (var file = new StreamWriter(Alphaleonis.Win32.Filesystem.File.OpenWrite(hashPath)))
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

        public static string GetEntryRoot(int entryId)
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

        private bool _baseObjectsSet;
        private readonly List<FileSystemObject> _oldObjects = new List<FileSystemObject>();
        private readonly Dictionary<string, FileSystemObject> _oldObjectsDict = new Dictionary<string, FileSystemObject>();

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
            var settings = new FileSystemObjectSettings(this)
            {
                Reporter = ErrorReporter,
                BackupModeMap = MakeMap(forLaterCheck),
            };
            BaseObjects.AddRange(GetCurrentSourceLocations().Select(MapPathForward).Select(x => FileSystemObject.Create(x, settings)));
            BaseObjects.ForEach(x => x.IsMain = true);
            while (forLaterCheck.Count > 0)
            {
                var oldForLaterCheck = forLaterCheck;
                forLaterCheck = new List<string>();
                settings.BackupModeMap = MakeMap(forLaterCheck);
                foreach (var path in oldForLaterCheck)
                    if (!Covered(path))
                        BaseObjects.Add(FileSystemObject.Create(MapPathForward(path), settings));
            }
            for (int i = 0; i < BaseObjects.Count; i++)
                BaseObjects[i].EntryNumber = i;
            RecalculateFileGuids();
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
                            PhysicalSize = fso.Size,
                            VirtualSize = fso.Size,
                            ZipPath = fso.ZipPath,
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
            "",
            "Ki",
            "Mi",
            "Gi",
            "Ti",
            "Pi",
            "Ei",
        };

        public static string FormatSize(long size)
        {
            double f = size;
            var index = 0;
            for (; f >= 1024.0 && index < Sizes.Length - 1; index++)
                f /= 1024.0;
            return String.Format("{0:0.#} {1}B", f, Sizes[index]);
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

        private readonly Queue<FilishFso> _recalculateFileGuids = new Queue<FilishFso>();

        public void EnqueueFileForGuidGet(FilishFso fso)
        {
            _recalculateFileGuids.Enqueue(fso);
        }

        private void RecalculateFileGuids()
        {
            while (_recalculateFileGuids.Count > 0)
            {
                var fso = _recalculateFileGuids.Dequeue();
                var path = fso.Path;
                path = MapPathBack(path);
                fso.SetFileSystemGuid(path, false);
            }
        }
    }
}
