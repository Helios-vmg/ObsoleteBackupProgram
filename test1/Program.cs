using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Schema;
using BackupEngine;
using BackupEngine.FileSystem;
using BackupEngine.FileSystem.FileSystemObjects;
using BackupEngine.Serialization;
using BackupEngine.Util;

namespace test1
{
#if true
    class Reporter : IErrorReporter
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

        private static readonly HashSet<string> DefaultIgnoredNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ".svn",
            ".git",
        };

        public HashSet<string> IgnoredExtensions = new HashSet<string>(DefaultIgnoredExtensions, StringComparer.InvariantCultureIgnoreCase);

        public HashSet<string> IgnoredPaths =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        public HashSet<string> IgnoredNames = new HashSet<string>(DefaultIgnoredExtensions,
            StringComparer.InvariantCultureIgnoreCase);

        public Backupper(string targetPath) : base(targetPath)
        {
        }

        public override BackupMode GetBackupModeForObject(FileSystemObject o, out bool followLinkTargets)
        {
            followLinkTargets = false;
            if (DefaultIgnoredNames.Contains(o.Name) ||
                    DefaultIgnoredExtensions.Contains(Path.GetExtension(o.Name)) ||
                    IgnoredPaths.Contains(o.Path))
                return BackupMode.NoBackup;
            return o.IsDirectoryish ? BackupMode.Directory : BackupMode.Full;
        }

        public override IEnumerable<string> GetSourceLocations()
        {
            return Sources;
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
            return 0;
        }

        public override int CompressionLevelForStructuralFiles
        {
            get { return 0; }
        }
    }
#endif

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

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern void test_func();

        static void Main(string[] args)
        {
#if true
            {
                var bu = new Backupper(@"c:\test\Backup");
                //var bu = new Backupper(@"g:\Backup\test\Backup");
                //bu.Sources.Add(@"C:\Test\mono");

                //bu.PerformBackup();
                bu.RestoreBackup();
            }
#else
            {
                var bu = new Backupper(@"g:\Backup\001");
                bu.Sources.Add(@"f:\Backups\test");

                bu.RestoreBackup();
                //bu.PerformBackup();
            }
#endif
            /*
            {
                var bu = new Backupper(@"G:\Backup\000");
                bu.RestoreBackup();
            }
            */
        }
    }
}
