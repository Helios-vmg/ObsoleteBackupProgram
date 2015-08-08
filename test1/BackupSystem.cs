using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Remoting.Messaging;
using BackupEngine;
using BackupEngine.FileSystem;

namespace test1
{
    class BackupSystem : BaseBackupEngine
    {
        public List<string> Sources = new List<string>();
        private readonly ErrorReporter _errorReporter = new ErrorReporter();

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

        public BackupSystem(string targetPath) : base(targetPath)
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
            get { return _errorReporter; }
        }

        public ChangeCriterium GlobalChangeCriterium = ChangeCriterium.Hash;

        public enum ChangeCriterium
        {
            ArchiveFlag,
            Size,
            Date,
            Hash,
            HashAuto,
        }

        public override BackupEngine.ChangeCriterium GetChangeCriterium(FileSystemObject newFile)
        {
            switch (GlobalChangeCriterium)
            {
                case ChangeCriterium.ArchiveFlag:
                    return BackupEngine.ChangeCriterium.ArchiveFlag;
                case ChangeCriterium.Size:
                    return BackupEngine.ChangeCriterium.Size;
                case ChangeCriterium.Date:
                    return BackupEngine.ChangeCriterium.Date;
                case ChangeCriterium.Hash:
                    return BackupEngine.ChangeCriterium.Hash;
                case ChangeCriterium.HashAuto:
                    return newFile.Size < 1024*1024 ? BackupEngine.ChangeCriterium.Hash : BackupEngine.ChangeCriterium.Date;
            }
            throw new ArgumentOutOfRangeException();
        }
    }
}
