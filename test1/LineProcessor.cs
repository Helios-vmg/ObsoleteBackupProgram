using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BackupEngine;
using BackupEngine.FileSystem;

namespace test1
{
    class LineProcessor
    {
        private BackupSystem _backupSystem;
        private int _selectedVersion;
        private int SelectedVersion
        {
            set
            {
                _selectedVersion = value;
                if (!UserMode)
                    return;
                using (new DisplayColor())
                    Console.WriteLine("Selected version: " + _selectedVersion);
            }
        }
        private enum OperationMode
        {
            Pipe,
            User,
        }

        private readonly OperationMode _operationMode;

        private readonly HashSet<string> _arguments;

        private bool UserMode
        {
            get { return _operationMode == OperationMode.User; }
        }

        public LineProcessor(IEnumerable<string> args)
        {
            _arguments = new HashSet<string>(args, StringComparer.CurrentCultureIgnoreCase);
            _operationMode = _arguments.Contains("--pipe") ? OperationMode.Pipe : OperationMode.User;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        private static string[] CommandLineToArgs(string commandLine)
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

        public void Process()
        {
            while (true)
            {
                if (UserMode)
                    Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null)
                    break;
                var array = CommandLineToArgs(line);
                if (array.Length == 0)
                    continue;
                if (array[0].Equals("quit", StringComparison.CurrentCultureIgnoreCase))
                    break;
                ProcessLine(array);
            }
        }

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
                case "if":
                    ProcessIf(line);
                    break;
                case "set":
                    ProcessSet(line);
                    break;
            }
        }

        private void ProcessSet(string[] line)
        {
            switch (line[1].ToLower())
            {
                case "use_snapshots":
                    ProcessSetUseSnapshots(line);
                    break;
                case "change_criterium":
                    ProcessSetChangeCriterium(line);
                    break;
            }
        }

        private void ProcessSetChangeCriterium(string[] line)
        {
            switch (line[2].ToLower())
            {
                case "archive_flag":
                    _backupSystem.GlobalChangeCriterium = BackupSystem.ChangeCriterium.ArchiveFlag;
                    break;
                case "size":
                    _backupSystem.GlobalChangeCriterium = BackupSystem.ChangeCriterium.Size;
                    break;
                case "date":
                    _backupSystem.GlobalChangeCriterium = BackupSystem.ChangeCriterium.Date;
                    break;
                case "hash":
                    _backupSystem.GlobalChangeCriterium = BackupSystem.ChangeCriterium.Hash;
                    break;
                case "hash_auto":
                    _backupSystem.GlobalChangeCriterium = BackupSystem.ChangeCriterium.HashAuto;
                    break;
            }
        }

        private void ProcessSetUseSnapshots(string[] line)
        {
            switch (line[2].ToLower())
            {
                case "true":
                    _backupSystem.UseSnapshots = true;
                    break;
                case "false":
                    _backupSystem.UseSnapshots = false;
                    break;
            }
        }

        private void ProcessIf(string[] line)
        {
            if (!_arguments.Contains(line[1]))
                return;
            ProcessLine(line.Skip(2).ToArray());
        }

        private void ProcessShow(string[] line)
        {
            switch (line[1].ToLower())
            {
                case "dependencies":
                    ProcessShowDependencies();
                    break;
                case "versions":
                    ProcessShowVersions();
                    break;
                case "version_count":
                    ProcessShowVersionCount();
                    break;
                case "paths":
                    ProcessShowPaths();
                    break;
            }
        }

        private void ProcessShowPaths()
        {
            EnsureExistingBackup();
            var entries = _backupSystem.GetEntries(_selectedVersion);
            int entryId = 0;
            if (UserMode)
            {
                using (new DisplayColor())
                {
                    foreach (var fileSystemObject in entries)
                    {
                        Console.WriteLine("Entry {0}, base: {1}", entryId++, fileSystemObject.BasePath);
                        fileSystemObject.Iterate(fso =>
                        {
                            Console.WriteLine(fso.PathWithoutBase);
                            Console.WriteLine("    Type: " + fso.Type);
                            if (fso.Type == FileSystemObjectType.RegularFile || fso.Type == FileSystemObjectType.FileHardlink)
                                Console.WriteLine("    Size: " + BaseBackupEngine.FormatSize(fso.Size));
                            if (fso.StreamUniqueId > 0)
                            {
                                Console.WriteLine("    Stream ID: " + fso.StreamUniqueId);
                                Console.WriteLine("    Stored in version: " + fso.LatestVersion);
                            }
                            if (fso.IsLinkish)
                                Console.WriteLine("    Link target: " + fso.Target);
                        });
                    }
                }
            }
        }

        private static string ToString(IEnumerable<int> list)
        {
            var ret = string.Join(", ", list);
            if (ret.Length == 0)
                ret = "None.";
            return ret;
        }

        private void ProcessShowVersions()
        {
            if (UserMode)
            {
                using (new DisplayColor())
                    Console.WriteLine("Available versions: " + ToString(_backupSystem.GetVersions()));
            }
        }

        private void ProcessShowVersionCount()
        {
            if (UserMode)
            {
                using (new DisplayColor())
                {
                    if (_backupSystem.VersionCount > 0)
                    {
                        Console.WriteLine("Available versions count: " + _backupSystem.GetVersions().Count());
                        Console.WriteLine("Latest version: " + (_backupSystem.VersionCount - 1));
                    }
                    else
                        Console.WriteLine("No versions.");
                }
            }
        }

        void EnsureExistingBackup()
        {
            if (_backupSystem == null)
                throw new Exception("No backup selected.");
            if (_backupSystem.VersionCount == 0)
                throw new Exception("Backup has never been performed.");
        }

        class TempColor : IDisposable
        {
            private readonly ConsoleColor _oldColor;

            protected TempColor(ConsoleColor newColor)
            {
                _oldColor = Console.ForegroundColor;
                Console.ForegroundColor = newColor;
            }

            public void Dispose()
            {
                Console.ForegroundColor = _oldColor;
            }
        }

        class DisplayColor : TempColor
        {
            public DisplayColor() : base(ConsoleColor.White)
            {
            }
        }

        private void ProcessShowDependencies()
        {
            EnsureExistingBackup();
            var dependencies = _backupSystem.GetVersionDependencies(_selectedVersion);
            if (UserMode)
            {
                using (new DisplayColor())
                {
                    Console.WriteLine("Dependencies: " + ToString(dependencies));
                    Console.WriteLine("Total: " + dependencies.Count);
                }
            }
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
                versionNumber = _backupSystem.VersionCount + versionNumber;
            if (!_backupSystem.VersionExists(versionNumber))
                throw new Exception("No such version in backup.");
            SelectedVersion = versionNumber;
        }

        public void ProcessOpen(string[] line)
        {
            _backupSystem = new BackupSystem(line[1]);
            _selectedVersion = _backupSystem.VersionCount - 1;
        }

        private void ProcessAdd(string[] line)
        {
            _backupSystem.Sources.Add(line[1]);
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
            _backupSystem.IgnoredExtensions.Add(line[2]);
        }

        public void ProcessExcludePath(string[] line)
        {
            _backupSystem.IgnoredPaths.Add(line[2]);
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
            _backupSystem.IgnoredNames[line[3]] = BackupSystem.NameIgnoreType.File;
        }

        public void ProcessExcludeNameDirs(string[] line)
        {
            _backupSystem.IgnoredNames[line[3]] = BackupSystem.NameIgnoreType.Directory;
        }

        public void ProcessExcludeNameAll(string[] line)
        {
            _backupSystem.IgnoredNames[line[3]] = BackupSystem.NameIgnoreType.All;
        }

        public void ProcessBackup()
        {
            _backupSystem.PerformBackup();
        }

        public void ProcessRestore()
        {
            _backupSystem.RestoreBackup();
        }
    }
}
