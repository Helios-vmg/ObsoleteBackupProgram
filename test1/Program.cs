using System;
using System.Collections.Generic;
using System.Globalization;
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
            /*
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
            */
        };

        private static readonly HashSet<string> DefaultIgnoredNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ".svn",
            ".git",
        };

        public HashSet<string> IgnoredExtensions = new HashSet<string>(DefaultIgnoredExtensions, StringComparer.InvariantCultureIgnoreCase);

        public HashSet<string> IgnoredPaths =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        [Flags]
        public enum NameIgnoreType
        {
            None = 0,
            File = 1,
            Directory = 2,
            All = 3,
        }

        public Dictionary<string, NameIgnoreType> IgnoredNames = new Dictionary<string, NameIgnoreType>(StringComparer.InvariantCultureIgnoreCase);

        public Backupper(string targetPath) : base(targetPath)
        {
        }

        public override BackupMode GetBackupModeForObject(FileSystemObject o, out bool followLinkTargets)
        {
            followLinkTargets = false;
            if (DefaultIgnoredNames.Contains(o.Name) ||
                    DefaultIgnoredExtensions.Contains(Path.GetExtension(o.Name)) && !o.IsDirectoryish ||
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

    class ProgramState
    {
        private Backupper _backupper;

        public void ProcessLine(string[] line)
        {
            switch (line[0].ToLower())
            {
                case "open":
                    ProcessOpen(line);
                    break;
                case "add":
                    ProcessAdd(line);
                    break;
                case "exclude":
                    ProcessExclude(line);
                    break;
                case "backup":
                    ProcessBackup();
                    break;
                case "restore":
                    ProcessRestore();
                    break;
            }
        }

        public void ProcessOpen(string[] line)
        {
            _backupper = new Backupper(line[1]);
        }

        private void ProcessAdd(string[] line)
        {
            _backupper.Sources.Add(line[1]);
        }

        public void ProcessExclude(string[] line)
        {
            switch (line[1].ToLower())
            {
                case "extension":
                    ProcessExcludeExtension(line);
                    break;
                case "path":
                    ProcessExcludePath(line);
                    break;
                case "name":
                    ProcessExcludePath(line);
                    break;
            }
        }

        public void ProcessExcludeExtension(string[] line)
        {
            _backupper.IgnoredExtensions.Add(line[2]);
        }

        public void ProcessExcludePath(string[] line)
        {
            _backupper.IgnoredPaths.Add(line[2]);
        }

        public void ProcessExcludeName(string[] line)
        {
            switch (line[2].ToLower())
            {
                case "files":
                    ProcessExcludeNameFiles(line);
                    break;
                case "dirs":
                    ProcessExcludeNameDirs(line);
                    break;
                case "all":
                    ProcessExcludeNameAll(line);
                    break;
            }
        }

        public void ProcessExcludeNameFiles(string[] line)
        {
            _backupper.IgnoredNames[line[3]] = Backupper.NameIgnoreType.File;
        }

        public void ProcessExcludeNameDirs(string[] line)
        {
            _backupper.IgnoredNames[line[3]] = Backupper.NameIgnoreType.Directory;
        }

        public void ProcessExcludeNameAll(string[] line)
        {
            _backupper.IgnoredNames[line[3]] = Backupper.NameIgnoreType.All;
        }

        public void ProcessBackup()
        {
            _backupper.PerformBackup();
        }

        public void ProcessRestore()
        {
            _backupper.RestoreBackup();
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

        static Queue<char> ToQueue(string s)
        {
            var ret = new Queue<char>();
            bool spaces = true;
            foreach (var c in s)
            {
                if (!spaces || c != ' ' && c != '\t')
                {
                    spaces = false;
                    ret.Enqueue(char.ToLower(c, CultureInfo.CurrentCulture));
                }
            }
            return ret;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        public static string[] CommandLineToArgs(string commandLine)
        {
            int argc;
            var argv = CommandLineToArgvW(commandLine, out argc);
            if (argv == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception();
            try
            {
                var args = new string[argc];
                for (var i = 0; i < args.Length; i++)
                {
                    var p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    args[i] = Marshal.PtrToStringUni(p);
                }

                return args;
            }
            finally
            {
                Marshal.FreeHGlobal(argv);
            }
        }

        static void Main(string[] args)
        {
            var state = new ProgramState();

            string line;
            while ((line = Console.ReadLine()) != null)
            {
                var array = CommandLineToArgs(line);
                if (array.Length == 0)
                    continue;
                if (array[0].Equals("quit", StringComparison.CurrentCultureIgnoreCase))
                    break;
                state.ProcessLine(array);
            }
        }
    }
}
