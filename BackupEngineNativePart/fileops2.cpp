#include "stdafx.h"
#include "MiscFunctions.h"
#include "ExportedFunctions.h"

struct AutoHandle{
	HANDLE handle;
	AutoHandle(HANDLE handle) : handle(handle){}
	~AutoHandle(){
		if (this->handle && this->handle != INVALID_HANDLE_VALUE)
			CloseHandle(this->handle);
	}
};

// Advanced file operations

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

static bool starts_with(const std::wstring &a, const wchar_t *b){
	auto l = wcslen(b);
	if (a.size() < l)
		return false;
	while (l--)
		if (a[l] != b[l])
			return false;
	return true;
}

int internal_get_reparse_point_target(const wchar_t *path, unsigned long *unrecognized, std::wstring *target_path, bool *is_symlink){
	const auto share_mode = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
	const auto flags = FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS;
	AutoHandle h = CreateFileW(path, 0, share_mode, nullptr, OPEN_EXISTING, flags, nullptr);
	if (h.handle == INVALID_HANDLE_VALUE)
		return GetLastError();

	USHORT size = 1 << 14;
	std::vector<char> tempbuf(size);
	while (true){
		REPARSE_DATA_BUFFER *buf = (REPARSE_DATA_BUFFER *)&tempbuf[0];
		buf->ReparseDataLength = size;
		DWORD cbOut;
		auto status = DeviceIoControl(h.handle, FSCTL_GET_REPARSE_POINT, nullptr, 0, buf, size, &cbOut, nullptr);
		auto error = GetLastError();
		if (status){
			switch (buf->ReparseTag){
				case IO_REPARSE_TAG_SYMLINK:
					{
						auto p = (char *)buf->SymbolicLinkReparseBuffer.PathBuffer + buf->SymbolicLinkReparseBuffer.SubstituteNameOffset;
						auto begin = (const wchar_t *)p;
						auto end = (const wchar_t *)(p + buf->SymbolicLinkReparseBuffer.SubstituteNameLength);
						if (!!target_path){
							target_path->assign(begin, end);
							if (starts_with(*target_path, L"\\??\\"))
								*target_path = target_path->substr(4);
						}
						if (is_symlink)
							*is_symlink = true;
					}
					break;
				case IO_REPARSE_TAG_MOUNT_POINT:
					{
						auto p = (char *)buf->MountPointReparseBuffer.PathBuffer + buf->MountPointReparseBuffer.SubstituteNameOffset;
						auto begin = (const wchar_t *)p;
						auto end = (const wchar_t *)(p + buf->MountPointReparseBuffer.SubstituteNameLength);
						if (!!target_path){
							target_path->assign(begin, end);
							if (starts_with(*target_path, L"\\??\\"))
								*target_path = target_path->substr(4);
						}
						if (is_symlink)
							*is_symlink = false;
					}
					break;
				default:
					if (unrecognized)
						*unrecognized = buf->ReparseTag;
					return ERROR_UNIDENTIFIED_ERROR;
			}
		}else{
			if (error == ERROR_MORE_DATA){
				size *= 2;
				tempbuf.resize(size);
				continue;
			}
			return error;
		}
		break;
	}
	return ERROR_SUCCESS;
}

EXPORT_THIS int get_reparse_point_target(const wchar_t *_path, unsigned long *unrecognized, string_callback_t f){
	*unrecognized = 0;
	auto path = path_from_string(_path);
	std::wstring result;
	int ret = internal_get_reparse_point_target(path.c_str(), unrecognized, &result, nullptr);
	f(result.c_str());
	return ret;
}

EXPORT_THIS int get_file_guid(const wchar_t *_path, GUID *guid){
	auto path = path_from_string(_path);
#define CREATE_FILE(x) CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, (x), nullptr)
	AutoHandle h = CREATE_FILE(0);
	if (h.handle == INVALID_HANDLE_VALUE)
		h.handle = CREATE_FILE(FILE_FLAG_OPEN_REPARSE_POINT);
	if (h.handle == INVALID_HANDLE_VALUE)
		return 1;

	FILE_OBJECTID_BUFFER buf;
	DWORD cbOut;
	int ret = 2;
	static const DWORD rounds[] = {
		FSCTL_CREATE_OR_GET_OBJECT_ID,
		FSCTL_GET_OBJECT_ID,
	};
	for (auto ctl : rounds){
		if (DeviceIoControl(h.handle, ctl, nullptr, 0, &buf, sizeof(buf), &cbOut, nullptr)){
			CopyMemory(guid, &buf.ObjectId, sizeof(GUID));
			ret = 0;
			break;
		}else{
			auto error = GetLastError();
			if (error == ERROR_WRITE_PROTECT)
				continue;
#ifdef _DEBUG
			if (error != ERROR_FILE_NOT_FOUND)
				std::cerr << "DeviceIoControl() in get_file_guid(): " << error << std::endl;
#endif
			break;
		}
	}
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
	AutoHandle handle = CreateFileW(path, 0, FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
	DWORD ret = 0;
	if (handle.handle == INVALID_HANDLE_VALUE)
		return ret;
	BY_HANDLE_FILE_INFORMATION info;
	if (GetFileInformationByHandle(handle.handle, &info))
		ret = info.nNumberOfLinks;
	return ret;
}

static bool internal_is_reparse_point(const wchar_t *path){
	DWORD attr = GetFileAttributesW(path);
	return (attr & FILE_ATTRIBUTE_REPARSE_POINT) == FILE_ATTRIBUTE_REPARSE_POINT;
}

EXPORT_THIS bool is_reparse_point(const wchar_t *_path){
	auto path = path_from_string(_path);
	return internal_is_reparse_point(path.c_str());
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
	return (unsigned)(is_symlink ? FileSystemObjectType::DirectorySymlink : FileSystemObjectType::Junction);
}

EXPORT_THIS int list_all_hardlinks(const wchar_t *_path, string_callback_t f){
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

int create_symlink(const wchar_t *_link_location, const wchar_t *_target_location, bool directory){
	auto link_location = path_from_string(_link_location);
	auto target_location = path_from_string(_target_location);
	if (CreateSymbolicLinkW(link_location.c_str(), target_location.c_str(), directory ? SYMBOLIC_LINK_FLAG_DIRECTORY : 0))
		return 0;
	return GetLastError();
}

EXPORT_THIS int create_symlink(const wchar_t *_link_location, const wchar_t *_target_location){
	return create_symlink(_link_location, _target_location, false);
}

EXPORT_THIS int create_directory_symlink(const wchar_t *_link_location, const wchar_t *_target_location){
	return create_symlink(_link_location, _target_location, true);
}

typedef struct {
	DWORD ReparseTag;
	WORD ReparseDataLength;
	WORD Reserved2;
	WORD Reserved;
	WORD ReparseTargetLength;
	WORD ReparseTargetMaximumLength;
	WORD Reserved1;
	WCHAR ReparseTarget[1];
} REPARSE_MOUNTPOINT_DATA_BUFFER, *PREPARSE_MOUNTPOINT_DATA_BUFFER;

EXPORT_THIS int create_junction(const wchar_t *_link_location, const wchar_t *_target_location){
	auto path = path_from_string(_link_location);
	AutoHandle h = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_DIRECTORY | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, nullptr);
	if (h.handle == INVALID_HANDLE_VALUE)
		return GetLastError();

	std::wstring target = L"\\??\\";
	target.append(_target_location);

	const auto byte_length = target.size() * sizeof(wchar_t);
	const auto byte_length_z = byte_length + sizeof(wchar_t);

	std::vector<char> buffer(offsetof(REPARSE_MOUNTPOINT_DATA_BUFFER, ReparseTarget) + byte_length_z + 2);
	REPARSE_MOUNTPOINT_DATA_BUFFER *rdb = (REPARSE_MOUNTPOINT_DATA_BUFFER *)&buffer[0];
	rdb->ReparseTag = IO_REPARSE_TAG_MOUNT_POINT;
	rdb->ReparseDataLength = (DWORD)(byte_length_z + 10);
	rdb->Reserved = 0;
	rdb->Reserved1 = 0;
	rdb->ReparseTargetLength = (WORD)byte_length;
	rdb->ReparseTargetMaximumLength = rdb->ReparseTargetLength + sizeof(wchar_t);
	memcpy(rdb->ReparseTarget, &target[0], byte_length);
	auto status = DeviceIoControl(h.handle, FSCTL_SET_REPARSE_POINT, &buffer[0], (DWORD)buffer.size(), nullptr, 0, nullptr, nullptr);
	DWORD error = ERROR_SUCCESS;
	if (!status)
		error = GetLastError();
	return error;
}

EXPORT_THIS int create_file_reparse_point(const wchar_t *_link_location, const wchar_t *_target_location){
	return E_FAIL;
}

EXPORT_THIS int create_hardlink(const wchar_t *_link_location, const wchar_t *_existing_file){
	auto link_location = path_from_string(_link_location);
	auto existing_file = path_from_string(_existing_file);
	if (CreateHardLinkW(link_location.c_str(), existing_file.c_str(), nullptr))
		return 0;
	return GetLastError();
}
