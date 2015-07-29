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

        public HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

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
            var nit = NameIgnoreType.None;
            if (IgnoredNames.TryGetValue(o.Name, out nit) && ((nit & NameIgnoreType.File) != 0 && !o.IsDirectoryish || (nit & NameIgnoreType.Directory) != 0 && o.IsDirectoryish))
                return BackupMode.NoBackup;
            if (IgnoredExtensions.Contains(Path.GetExtension(o.Name)) && !o.IsDirectoryish ||
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
        private int _selectedVersion;

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
                case "select":
                    ProcessSelect(line);
                    break;
                case "show":
                    ProcessShow(line);
                    break;
            }
        }

        private void ProcessShow(string[] line)
        {
            switch (line[1].ToLower())
            {
                case "dependencies":
                    ProcessShowDependencies();
                    break;
            }
        }

        void EnsureExistingBackup()
        {
            if (_backupper == null)
                throw new Exception("No backup selected.");
            if (_backupper.VersionCount == 0)
                throw new Exception("Backup has never been performed.");
        }

        private void ProcessShowDependencies()
        {
            EnsureExistingBackup();
            var dependencies = _backupper.GetVersionDependencies(_selectedVersion);
            Console.WriteLine("Dependencies: " + (dependencies.Count == 0 ? "None." : string.Join(", ", dependencies)));
        }

        private void ProcessSelect(string[] line)
        {
            switch (line[1].ToLower())
            {
                case "version":
                    ProcessSelectVersion(line);
                    break;
            }
        }

        private void ProcessSelectVersion(string[] line)
        {
            var versionNumber = Convert.ToInt32(line[2]);
            EnsureExistingBackup();
            if (versionNumber < 0)
                versionNumber = _backupper.VersionCount + versionNumber;
            if (versionNumber < 0 || versionNumber >= _backupper.VersionCount)
                throw new Exception("No such version in backup.");
            _selectedVersion = versionNumber;
        }

        public void ProcessOpen(string[] line)
        {
            _backupper = new Backupper(line[1]);
            _selectedVersion = _backupper.VersionCount - 1;
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
                    ProcessExcludeName(line);
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
