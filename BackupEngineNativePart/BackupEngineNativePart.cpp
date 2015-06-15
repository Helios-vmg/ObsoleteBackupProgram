#include "stdafx.h"
#include "MiscFunctions.h"

#define EXPORT_THIS extern "C" _declspec(dllexport)

typedef void (*callback)(const wchar_t *);

static bool internal_exists(const wchar_t *path){
	DWORD attr = GetFileAttributesW(path);
	return !!~attr;
}

static bool exists(const wchar_t *_path){
	auto path = path_from_string(_path);
	return internal_exists(path.c_str());
}

static bool internal_is_reparse_point(const wchar_t *path){
	DWORD attr = GetFileAttributesW(path);
	return (attr & FILE_ATTRIBUTE_REPARSE_POINT) == FILE_ATTRIBUTE_REPARSE_POINT;
}

EXPORT_THIS bool is_reparse_point(const wchar_t *_path){
	auto path = path_from_string(_path);
	return internal_is_reparse_point(path.c_str());
}

typedef struct _REPARSE_DATA_BUFFER {
    ULONG  ReparseTag;
    USHORT ReparseDataLength;
    USHORT Reserved;
    union {
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            ULONG Flags;
            WCHAR PathBuffer[1];
        } SymbolicLinkReparseBuffer;
        struct {
            USHORT SubstituteNameOffset;
            USHORT SubstituteNameLength;
            USHORT PrintNameOffset;
            USHORT PrintNameLength;
            WCHAR PathBuffer[1];
        } MountPointReparseBuffer;
        struct {
            UCHAR  DataBuffer[1];
        } GenericReparseBuffer;
    } DUMMYUNIONNAME;
} REPARSE_DATA_BUFFER, *PREPARSE_DATA_BUFFER;

static int internal_get_reparse_point_target(const wchar_t *path, unsigned long *unrecognized, std::wstring *target_path, bool *is_symlink){
	HANDLE h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, nullptr);
	std::wstring ret;
	if (h == INVALID_HANDLE_VALUE)
		return 1;

	USHORT size = 1 << 14;
	std::vector<char> tempbuf(size);
	REPARSE_DATA_BUFFER *buf = (REPARSE_DATA_BUFFER *)&tempbuf[0];
	buf->ReparseDataLength = size;
	DWORD cbOut;
	auto status = DeviceIoControl(h, FSCTL_GET_REPARSE_POINT, nullptr, 0, buf, size, &cbOut, nullptr);
	auto error = GetLastError();
	if (status){
		switch (buf->ReparseTag){
			case IO_REPARSE_TAG_SYMLINK:
				{
					auto p = (char *)buf->SymbolicLinkReparseBuffer.PathBuffer + buf->SymbolicLinkReparseBuffer.SubstituteNameOffset;
					auto begin = (const wchar_t *)p;
					auto end = (const wchar_t *)(p + buf->SymbolicLinkReparseBuffer.SubstituteNameLength);
					ret = std::wstring(begin, end);
					if (is_symlink)
						*is_symlink = true;
				}
				break;
			case IO_REPARSE_TAG_MOUNT_POINT:
				{
					auto p = (char *)buf->MountPointReparseBuffer.PathBuffer + buf->MountPointReparseBuffer.SubstituteNameOffset;
					auto begin = (const wchar_t *)p;
					auto end = (const wchar_t *)(p + buf->MountPointReparseBuffer.SubstituteNameLength);
					ret = std::wstring(begin, end);
					if (is_symlink)
						*is_symlink = false;
				}
				break;
			default:
				if (unrecognized)
					*unrecognized = buf->ReparseTag;
				return 2;
		}
	}
	CloseHandle(h);
	if (target_path)
		*target_path = ret;
	return 0;
}

EXPORT_THIS int get_reparse_point_target(const wchar_t *_path, unsigned long *unrecognized, callback f){
	*unrecognized = 0;
	auto path = path_from_string(_path);
	std::wstring result;
	int ret = internal_get_reparse_point_target(path.c_str(), unrecognized, &result, nullptr);
	f(result.c_str());
	return ret;
}

EXPORT_THIS int get_file_guid(const wchar_t *_path, GUID *guid){
	auto path = path_from_string(_path);
	HANDLE h = CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
	if (h == INVALID_HANDLE_VALUE)
		return 1;

	FILE_OBJECTID_BUFFER buf;
	DWORD cbOut;
	int ret = 0;
	if (DeviceIoControl(h, FSCTL_CREATE_OR_GET_OBJECT_ID, nullptr, 0, &buf, sizeof(buf), &cbOut, nullptr))
		CopyMemory(guid, &buf.ObjectId, sizeof(GUID));
	else
		ret = 2;
	CloseHandle(h);
	return ret;
}

static bool is_directory(const wchar_t *path){
	return (GetFileAttributesW(path) & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY;
}

static bool has_file_size(const wchar_t *path){
	WIN32_FILE_ATTRIBUTE_DATA fad;
	return !(!GetFileAttributesExW(path, GetFileExInfoStandard, &fad) || (fad.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY);
}

enum class FileSystemObjectType{
	Unknown = 0,
	Directory,
    RegularFile,
    DirectorySymlink,
    Junction,
    FileSymlink,
    FileReparsePoint,
    FileHardlink,
};

static DWORD hardlink_count(const wchar_t *path){
	auto handle = CreateFileW(path, 0, FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
	DWORD ret = 0;
	if (handle == INVALID_HANDLE_VALUE)
		return ret;
	BY_HANDLE_FILE_INFORMATION info;
	if (GetFileInformationByHandle(handle, &info))
		ret = info.nNumberOfLinks;
	CloseHandle(handle);
	return ret;
}

EXPORT_THIS unsigned get_file_system_object_type(const wchar_t *_path){
	auto path = path_from_string(_path);
	//auto has_file_size = ::has_file_size(path.c_str());

	bool is_symlink = false;
	bool is_rp = internal_is_reparse_point(path.c_str());
	//if (has_file_size){
	if (!is_directory(path.c_str())){
		if (!is_rp)
			return (unsigned)(hardlink_count(path.c_str()) < 2 ? FileSystemObjectType::RegularFile : FileSystemObjectType::FileHardlink);
		internal_get_reparse_point_target(path.c_str(), nullptr, nullptr, &is_symlink);
		return (unsigned)(is_symlink ? FileSystemObjectType::FileSymlink : FileSystemObjectType::FileReparsePoint);
	}

	if (!is_rp)
		return (unsigned)FileSystemObjectType::Directory;
	std::wstring target;
	internal_get_reparse_point_target(path.c_str(), nullptr, &target, &is_symlink);
#if 0
	if (!exists(target.c_str()))
		return (unsigned)FileSystemObjectType::Unknown;
	
	auto preliminary = (FileSystemObjectType)get_file_system_object_type(target.c_str());
	switch (preliminary){
		case FileSystemObjectType::Directory:
		case FileSystemObjectType::DirectorySymlink:
		case FileSystemObjectType::Junction:
			return (unsigned)(is_symlink ? FileSystemObjectType::DirectorySymlink : FileSystemObjectType::Junction);
		default:
			return (unsigned)FileSystemObjectType::FileSymlink;
	}
#else
	return (unsigned)(is_symlink ? FileSystemObjectType::DirectorySymlink : FileSystemObjectType::Junction);
#endif
}

EXPORT_THIS int list_all_hardlinks(const wchar_t *_path, callback f){
	auto path = path_from_string(_path);
	HANDLE handle;
	const DWORD default_size = 1 << 10;
	{
		DWORD size = default_size;
		std::vector<wchar_t> buffer(size);
		while (1){
			handle = FindFirstFileNameW(path.c_str(), 0, &size, &buffer[0]);
			if (handle == INVALID_HANDLE_VALUE){
				auto error = GetLastError();
				if (error == ERROR_MORE_DATA){
					buffer.resize(size);
					continue;
				}
				return error;
			}
			break;
		}
		buffer.resize(size);
		buffer.push_back(0);
		f(std::wstring(&buffer[0], &buffer[buffer.size() - 1]).c_str());
	}
	int ret = 0;
	while (1){
		bool Continue = true;
		DWORD size = default_size;
		std::vector<wchar_t> buffer(size);
		while (1){
			Continue = !!FindNextFileNameW(handle, &size, &buffer[0]);
			if (!Continue){
				auto error = GetLastError();
				if (error == ERROR_MORE_DATA){
					buffer.resize(size);
					continue;
				}
				if (error == ERROR_HANDLE_EOF)
					break;
				ret = error;
				break;
			}
		}
		if (!Continue)
			break;
		buffer.resize(size);
		buffer.push_back(0);
		f(std::wstring(&buffer[0], &buffer[buffer.size() - 1]).c_str());
	}
	FindClose(handle);
	return ret;
}

EXPORT_THIS int get_file_size(__int64 *dst, const wchar_t *_path){
	*dst = 0;
	auto path = path_from_string(_path);
	WIN32_FILE_ATTRIBUTE_DATA fad;
	if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &fad))
		return GetLastError();
	*dst = (((unsigned __int64)fad.nFileSizeHigh) << 32) | ((unsigned __int64)fad.nFileSizeLow);
	return 0;
}
