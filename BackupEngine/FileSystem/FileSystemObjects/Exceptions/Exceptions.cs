using System;

namespace BackupEngine.FileSystem.FileSystemObjects.Exceptions
{
    public class InvalidBackupMode : Exception
    {
        public InvalidBackupMode(string message)
            : base(message)
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

    public class InvalidBackup : Exception
    {
        public InvalidBackup(string path, string reason = null)
            : base("Backup at \"" + path + "\" is invalid." + (!string.IsNullOrEmpty(reason) ? "Reason: " + reason : string.Empty))
        {
        }
    }

    public class ReparsePointsNotImplemented : NotImplementedException
    {
        public ReparsePointsNotImplemented(string path) :
            base("Restoring reparse points is not implemented, therefore " + 
            "backing up reparse points is not allowed. To back up, replace this" +
            "reparse point with a symbolic link: " + path)
        {
        }
    }
}
