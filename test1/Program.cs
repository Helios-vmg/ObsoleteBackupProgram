using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Schema;
using BackupEngine;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.Serialization;
using BackupEngine.Util;

namespace test1
{
    class Reporter : BackupEngine.IErrorReporter
    {
        public bool ReportError(Exception e, string context = null)
        {
            var sb = new StringBuilder();
            sb.Append("Exception was thrown");
            if (!string.IsNullOrEmpty(context))
            {
                sb.Append(" while ");
                sb.Append(context);
            }
            sb.Append(": ");
            sb.Append(e.Message);
            Console.WriteLine(sb.ToString());
            return true;
        }
    }

    class Backupper : BaseBackupEngine
    {
        public List<string> Sources = new List<string>();
        public string Destination;
        private readonly Reporter _reporter = new Reporter();

        private static readonly HashSet<string> DefaultIgnoredExtensions = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ".img",
            ".sdf",
            ".ipch",
            ".pch",
            ".sqlite",
            ".lib",
            ".exe",
            ".obj",
            ".bz2",
            ".a",
            ".pdb",
            ".dll",
            ".7z",
            ".so",
            ".o",
            ".ilk",
            ".tlog",
            ".pack",
            ".zip",
            ".tar",
        };

        public HashSet<string> IgnoredExtensions = DefaultIgnoredExtensions;

        public HashSet<string> IgnoredPaths =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        public override BackupMode GetBackupModeForObject(FileSystemObject o, out bool followLinkTargets)
        {
            followLinkTargets = false;
            if (DefaultIgnoredExtensions.Contains(Path.GetExtension(o.Name)) || IgnoredPaths.Contains(o.Path))
                return BackupMode.NoBackup;
            return o.IsDirectoryish ? BackupMode.Directory : BackupMode.Full;
        }

        public override IEnumerable<string> GetSourceLocations()
        {
            return Sources;
        }

        public override string TargetLocation
        {
            get { return Destination; }
        }

        public override IErrorReporter ErrorReporter
        {
            get { return _reporter; }
        }

        public override HashType HashAlgorithm
        {
            get { return HashType.Sha256; }
        }

        public override int CompressionLevelForFile(FileSystemObject fso)
        {
            return 7;
        }
    }

    class Program
    {
        private static BackupMode Filter(FileSystemObject fso)
        {
            switch (fso.Type)
            {
                case FileSystemObjectType.Directory:
                case FileSystemObjectType.DirectorySymlink:
                case FileSystemObjectType.Junction:
                    return BackupMode.Directory;
                case FileSystemObjectType.RegularFile:
                case FileSystemObjectType.FileSymlink:
                case FileSystemObjectType.FileReparsePoint:
                case FileSystemObjectType.FileHardlink:
                    return BackupMode.Full;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static void Main(string[] args)
        {
            var bu = new Backupper
            {
                Destination = @"G:\Backup\000",
            };
            bu.Sources.Add(@"F:\Data\Programming");
            bu.IgnoredPaths.Add(@"F:\Data\Programming\Android");
            bu.IgnoredPaths.Add(@"F:\Data\Programming\Visual Studio 2012\Projects\BackupEngine");

#if false
            List<FileSystemObject> files;
            List<DirectoryFso> dirs;
            List<Tuple<string, long>> exts;
            bu.GenerateSizeReport(out dirs, out files, out exts);

            dirs.Where(x => x.RecursiveSize > 1024*1024).ForEach(x => Console.WriteLine(x.Path + "\t" + BaseBackupEngine.FormatSize(x.RecursiveSize)));
            Console.WriteLine("");
            files.Where(x => x.RecursiveSize > 1024 * 1024).ForEach(x => Console.WriteLine(x.Path + "\t" + BaseBackupEngine.FormatSize(x.RecursiveSize)));
            Console.WriteLine("");
            exts.Where(x => x.Item2 > 1024 * 1024).ForEach(x => Console.WriteLine(x.Item1 + "\t" + BaseBackupEngine.FormatSize(x.Item2)));
#endif
            bu.PerformBackup();
            var fso = BaseBackupEngine.TestRead(@"g:\Backup\000\version0.zip");
            using (var file = new FileStream("output.json", FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(file, Encoding.UTF8))
            {
                writer.Write(Serializer.Serialize(fso));
            }
        }
    }
}
