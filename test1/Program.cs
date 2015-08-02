using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using BackupEngine;
using BackupEngine.FileSystem;
using BackupEngine.Util;

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void StringResultCallback(string path, uint driveType);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int enumerate_volumes(StringResultCallback callback);

        static DriveType toDriveType(uint type)
        {
            switch (type)
            {
                case 0:
                    return DriveType.Unknown;
                case 1:
                    return DriveType.NoRootDirectory;
                case 2:
                    return DriveType.Removable;
                case 3:
                    return DriveType.Fixed;
                case 4:
                    return DriveType.Network;
                case 5:
                    return DriveType.CDRom;
                case 6:
                    return DriveType.Ram;
            }
            return DriveType.Unknown;
        }

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int create_snapshot(out IntPtr handle);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int add_volume_to_snapshot(IntPtr handle, string volume);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int do_snapshot(IntPtr handle, string basePath);

        class ShadowInfo
        {
            public Guid ShadowId;
            public int SnapshotsCount;
            public string SnapshotDeviceObject;
            public string OriginalVolumeName;
            public string OriginatingMachine;
            public string ServiceMachine;
            public string ExposedName;
            public string ExposedPath;
            public Guid ProviderId;
            public int SnapshotAttributes;
            public long CreatedAt;
            public int Status;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public delegate void GetSnapshotPropertiesCallback(Guid shadowId, int snapshotsCount, string snapshotDeviceObject, string originalVolumeName, string originatingMachine, string serviceMachine, string exposedName, string exposedPath, Guid providerId, int snapshotAttributes, long createdAt, int status);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern void get_snapshot_properties(IntPtr handle, out Guid snapshotId, GetSnapshotPropertiesCallback callback);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern int release_snapshot(IntPtr handle);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        public static extern void test_func(string path);

        static void Main(string[] args)
        {
            IntPtr handle = IntPtr.Zero;
            Console.WriteLine("create_snapshot()");
            var error = create_snapshot(out handle);
            if (error != 0)
            {
                Console.WriteLine("Error: " + error);
                return;
            }
            var basePath = @"c:\a";
            try
            {
                Console.WriteLine("add_volume_to_snapshot()");
                error = add_volume_to_snapshot(handle, @"f:\");
                if (error != 0)
                {
                    Console.WriteLine("Error: " + error);
                    return;
                }
                Console.WriteLine("do_snapshot()");
                Directory.CreateDirectory(basePath);
                error = do_snapshot(handle, basePath);
                if (error != 0)
                {
                    Console.WriteLine("Error: " + error);
                    return;
                }
                Guid snapshotSetId;
                var shadows = new List<ShadowInfo>();
                Console.WriteLine("get_snapshot_properties()");
                get_snapshot_properties(handle, out snapshotSetId, (shadowId, snapshotsCount, snapshotDeviceObject, originalVolumeName, originatingMachine, serviceMachine, exposedName, exposedPath, providerId, snapshotAttributes, createdAt, status) => shadows.Add(new ShadowInfo
                {
                    ShadowId = shadowId,
                    SnapshotsCount = snapshotsCount,
                    SnapshotDeviceObject = snapshotDeviceObject,
                    OriginalVolumeName = originalVolumeName,
                    OriginatingMachine = originatingMachine,
                    ServiceMachine = serviceMachine,
                    ExposedName = exposedName,
                    ExposedPath = exposedPath,
                    ProviderId = providerId,
                    SnapshotAttributes = snapshotAttributes,
                    CreatedAt = createdAt,
                    Status = status,
                }));
                Console.WriteLine("Snapshot set ID: " + snapshotSetId);
                foreach (var shadow in shadows)
                {
                    Console.WriteLine("-----------------");
                    Console.WriteLine("Shadow ID: " + shadow.ShadowId);
                    Console.WriteLine("Snapshots count: " + shadow.SnapshotsCount);
                    Console.WriteLine("Snapshots device object: " + shadow.SnapshotDeviceObject);
                    Console.WriteLine("Original volume name: " + shadow.OriginalVolumeName);
                    Console.WriteLine("Originating machine: " + shadow.OriginatingMachine);
                    Console.WriteLine("Service machine: " + shadow.ServiceMachine);
                    Console.WriteLine("Exposed name: " + shadow.ExposedName);
                    Console.WriteLine("Exposed path: " + shadow.ExposedPath);
                    Console.WriteLine("Provider ID: " + shadow.ProviderId);
                    Console.WriteLine("Snapshot attributes: " + shadow.SnapshotAttributes);
                    Console.WriteLine("Created at: " + shadow.CreatedAt);
                    Console.WriteLine("Status: " + shadow.Status);

                    test_func(shadow.SnapshotDeviceObject + @"\*");

                    /*
                    var helloPath =
                        @"f:\data\programming\visual studio 2013\projects\backupengine\bin64\hello.txt";
                    
                    using (var file = new FileStream(helloPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        var path = helloPath.Replace(@"f:\", shadow.exposedName + @"\");
                        Console.WriteLine(path);
                        using (var file2 = new StreamReader(path))
                        {
                            Console.WriteLine(file2.ReadToEnd());
                        }
                    }
                    */
                }
            }
            finally
            {
                release_snapshot(handle);
                //Directory.Delete(basePath, true);
            }


            //var processor = new LineProcessor(args);
            //processor.Process();
        }

    }
}
