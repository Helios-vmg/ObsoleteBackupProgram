# BackupProgram
## Introduction
BackupProgram is a directory backup utility for NTFS under Windows Vista and newer, designed with an emphasis on preserving as much file system metadata as possible.

## Features
|NTFS Feature|Supported|
|-|-|
|Regular files and directories|Yes|
|File and directory symlinks|Yes|
|Directory junctions|Yes|
|File junctions|No[^filejunctions]|
|Hardlinks|Yes|
|Alternate streams|No|
|ACLs|No|
|File attributes|No|
|File times|No|

[^filejunctions]: Although the file system has the concept of file junctions, the Windows API does not allow creating them, and thus it is impossible for users to create them.

|Backup Feature|Supported|
|-|-|
|Full backups|Yes|
|Incremental backups|Yes|
|Whole-version compression|Yes (LZMA)|
|Encryption|Yes (RSA)|
|File-level deltas|Yes|
|Block-level deltas (rdiff)|Coming soon|
|Backing up files in use|Yes (through VSS)|
|Transacted backups|Yes (through TxF)|
|Transacted restores|No|
|Deduplication|No|
|Move & rename detection|No|

## Limitations and known issues
- VSS does not work with nested file systems (e.g. an NTFS volume in a VHD stored in an NTFS volume, or an NTFS volume in a TrueCrypt volume in an NTFS volume). However, with rdiff it's possible to make space-efficient backups of virtual hard disks, although encripted file systems are incompressible.
- Backing up locked files in a network share is not supported.
- Transacted writes to network shares are not supported. Writing the backup files to a network share will work, but the process will not be transacted. This means that the backup version that was being generated may be left incomplete, and thus corrupted, in case of a power failure on either machine. Once power is restored, it is possible to run a verification on the archive.
- Hardlinks are detected by examining and generating file GUIDs. When using VSS, it is possible for the situation to arise that one of two files (both of which were hardlinks with each other at the time the VSS snapshot was taken) may be deleted after the VSS snapshot is taken but before the file GUIDs are generated. If this happens, the two files may be treated as two unrelated regular files and stored redundantly.