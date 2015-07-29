using System;
using System.Collections.Generic;
using System.Globalization;
using BackupEngine;
using BackupEngine.FileSystem;

namespace test1
{
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
            var processor = new LineProcessor(args);
            processor.Process();
        }
    }
}
