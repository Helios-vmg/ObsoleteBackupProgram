﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BackupEngine.FileSystem.FileSystemObjects;

namespace BackupEngine.FileSystem
{
    public class UnrecognizedReparseTagException : Exception
    {
        public UnrecognizedReparseTagException(uint tag) : base("Unrecognized reparse tag: " + tag)
        {
        }
    }

    public class UnableToObtainGuid : Exception
    {
        public UnableToObtainGuid(string path)
            : base("Unable to obtain GUID from path: " + path)
        {
        }
    }
    
    public class UnableToDetermineFileSystemObjectType : Exception
    {
        public UnableToDetermineFileSystemObjectType(string path)
            : base("Unable to determine type of: " + path)
        {
        }
    }

    public static class FileSystemOperations
    {
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool is_reparse_point(string path);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void StringResultCallback(string s);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int get_reparse_point_target(string path, out uint unrecognizedReparseTag, StringResultCallback callback);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int get_file_guid(string path, out Guid guid);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint get_file_system_object_type(string path);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern int list_all_hardlinks(string path, StringResultCallback f);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern int get_file_size(out long size, string path);

        public static bool PathIsReparsePoint(string path)
        {
            return is_reparse_point(path);
        }

        public static string GetReparsePointTarget(string path)
        {
            uint reparseTag;
            string ret = null;
            var result = get_reparse_point_target(path, out reparseTag, x => ret = x);
            switch (result)
            {
                case 0:
                    return ret;
                case 1:
                    throw new FileNotFoundException("Not found: " + path);
                case 2:
                    throw new UnrecognizedReparseTagException(reparseTag);
                default:
                    throw new IndexOutOfRangeException();
            }
        }

        public static Guid GetFileGuid(string path)
        {
            var ret = new Guid();
            var result = get_file_guid(path, out ret);
            switch (result)
            {
                case 0:
                    return ret;
                case 1:
                    throw new FileNotFoundException("Not found: " + path);
                case 2:
                    throw new UnableToObtainGuid(path);
                default:
                    throw new IndexOutOfRangeException();
            }
        }

        private static readonly FileSystemObjectType[] FileSystemObjectTypeValues =
        {
            FileSystemObjectType.Directory,
            FileSystemObjectType.RegularFile,
            FileSystemObjectType.DirectorySymlink,
            FileSystemObjectType.Junction,
            FileSystemObjectType.FileSymlink,
            FileSystemObjectType.FileReparsePoint,
            FileSystemObjectType.FileHardlink,
        };

        public static FileSystemObjectType GetFileSystemObjectType(string path)
        {
            var ret = get_file_system_object_type(path);
            if (ret == 0 || ret - 1 >= FileSystemObjectTypeValues.Length)
                throw new UnableToDetermineFileSystemObjectType(path);
            return FileSystemObjectTypeValues[ret - 1];
        }

        public static List<string> ListAllHardlinks(string path)
        {
            var ret = new List<string>();
            var result = list_all_hardlinks(path, ret.Add);
            if (result == 0)
                return ret;
            throw new Win32Exception(result);
        }

        public static long GetFileSize(string path)
        {
            long ret;
            var result = get_file_size(out ret, path);
            if (result != 0)
                throw new Win32Exception(result);
            return ret;
        }
    }
}
