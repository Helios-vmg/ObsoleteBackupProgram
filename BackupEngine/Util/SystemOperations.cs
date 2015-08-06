using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BackupEngine.FileSystem;

namespace BackupEngine.Util
{
    public static class SystemOperations
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void EnumerateVolumesCallback(string path, string volumeLabel, uint driveType);

        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int enumerate_volumes(EnumerateVolumesCallback callback);

        internal static DriveType ToDriveType(uint type)
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

        public class VolumeInfo
        {
            public string VolumePath;
            public string VolumeLabel;
            public DriveType DriveType;
            public readonly List<string> MountedPaths = new List<string>();

            [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern int enumerate_mounted_paths(string volumePath, FileSystemOperations.StringResultCallback callback);
            
            internal VolumeInfo(string volume, string volumeLabel, uint type)
            {
                VolumePath = volume;
                VolumeLabel = volumeLabel;
                DriveType = ToDriveType(type);
                enumerate_mounted_paths(VolumePath, MountedPaths.Add);
            }

            public override string ToString()
            {
                var ret = new StringBuilder();
                ret.Append("(\"");
                ret.Append(VolumePath);
                ret.Append("\", \"");
                ret.Append(VolumeLabel);
                ret.Append("\", ");
                ret.Append(DriveType);
                ret.Append(", [");
                for (var i = 0; i < MountedPaths.Count; i++)
                {
                    ret.Append(i > 0 ? ", \"" : "\"");
                    ret.Append(MountedPaths[i]);
                    ret.Append("\"");
                }
                ret.Append("])");
                return ret.ToString();
            }
        }

        public static List<VolumeInfo> EnumerateVolumes()
        {
            var ret = new List<VolumeInfo>();
            var result = enumerate_volumes((v, l, t) => ret.Add(new VolumeInfo(v, l, t)));
            if (result != 0)
                throw new Win32Exception(result);
            return ret;
        }

        public class ShadowInfo
        {
            public readonly Guid ShadowId;
            public readonly int SnapshotsCount;
            public readonly string SnapshotDeviceObject;
            public readonly string OriginalVolumeName;
            public readonly string OriginatingMachine;
            public readonly string ServiceMachine;
            public readonly string ExposedName;
            public readonly string ExposedPath;
            public readonly Guid ProviderId;
            public readonly int SnapshotAttributes;
            public readonly DateTime CreatedAt;
            public readonly int Status;

            public ShadowInfo(Guid shadowId, int snapshotsCount, string snapshotDeviceObject,
                string originalVolumeName, string originatingMachine, string serviceMachine, string exposedName,
                string exposedPath, Guid providerId, int snapshotAttributes, long createdAt, int status)
            {
                ShadowId = shadowId;
                SnapshotsCount = snapshotsCount;
                SnapshotDeviceObject = snapshotDeviceObject;
                OriginalVolumeName = originalVolumeName;
                OriginatingMachine = originatingMachine;
                ServiceMachine = serviceMachine;
                ExposedName = exposedName;
                ExposedPath = exposedPath;
                ProviderId = providerId;
                SnapshotAttributes = snapshotAttributes;
                CreatedAt = DateTime.FromFileTimeUtc(createdAt);
                Status = status;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void GetSnapshotPropertiesCallback(Guid shadowId, int snapshotsCount, string snapshotDeviceObject, string originalVolumeName, string originatingMachine, string serviceMachine, string exposedName, string exposedPath, Guid providerId, int snapshotAttributes, long createdAt, int status);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int create_snapshot(out IntPtr handle);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int add_volume_to_snapshot(IntPtr handle, string volume);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int do_snapshot(IntPtr handle);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern void get_snapshot_properties(IntPtr handle, out Guid snapshotId, GetSnapshotPropertiesCallback callback);
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int release_snapshot(IntPtr handle);

        public class VolumeSnapshot : IDisposable
        {
            private IntPtr _handle;

            public readonly Guid SnapshotId;

            private readonly List<ShadowInfo> _shadows = new List<ShadowInfo>();
            private readonly List<string> _failedVolumes = new List<string>();
            
            public IEnumerable<ShadowInfo> Shadows
            {
                get { return _shadows; }
            }

            public IEnumerable<string> FailedVolumes
            {
                get { return _failedVolumes; }
            }

            private void PropsCallback(Guid shadowId, int snapshotsCount, string snapshotDeviceObject,
                string originalVolumeName, string originatingMachine, string serviceMachine, string exposedName,
                string exposedPath, Guid providerId, int snapshotAttributes, long createdAt, int status)
            {
                _shadows.Add(new ShadowInfo(shadowId, snapshotsCount, snapshotDeviceObject, originalVolumeName,
                    originatingMachine, serviceMachine, exposedName, exposedPath, providerId, snapshotAttributes,
                    createdAt, status));
            }

            private const uint VSS_E_UNEXPECTED_PROVIDER_ERROR = 0x8004230F;
            private const uint VSS_E_NESTED_VOLUME_LIMIT = 0x8004232C;

            public VolumeSnapshot(IEnumerable<string> volumes)
            {
                var error = create_snapshot(out _handle);
                if (error < 0)
                    Marshal.ThrowExceptionForHR(error);
                foreach (var volume in volumes)
                {
                    error = add_volume_to_snapshot(_handle, volume);
                    var uerror = IntegerOperations.UncheckedToUint32(error);
                    if (uerror == VSS_E_UNEXPECTED_PROVIDER_ERROR || uerror == VSS_E_NESTED_VOLUME_LIMIT)
                    {
                        _failedVolumes.Add(volume);
                        continue;
                    }
                    if (error < 0)
                        Marshal.ThrowExceptionForHR(error);
                }
                error = do_snapshot(_handle);
                if (error < 0)
                    Marshal.ThrowExceptionForHR(error);
                get_snapshot_properties(_handle, out SnapshotId, PropsCallback);
            }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero)
                {
                    release_snapshot(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }
    }
}
