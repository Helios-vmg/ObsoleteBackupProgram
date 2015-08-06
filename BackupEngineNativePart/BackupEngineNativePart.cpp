#include "stdafx.h"
#include "MiscFunctions.h"
#include "ExportedFunctions.h"

static bool internal_exists(const wchar_t *path){
	DWORD attr = GetFileAttributesW(path);
	return !!~attr;
}

static bool exists(const wchar_t *_path){
	auto path = path_from_string(_path);
	return internal_exists(path.c_str());
}

static void enumerate_volumes_helper(const wchar_t *volume, enumerate_volumes_callback_t cb){
	std::wstring temp = volume;
	temp.resize(temp.size() - 1);
	auto handle = CreateFileW(temp.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
	wchar_t volume_name[MAX_PATH * 2];
	if (handle != INVALID_HANDLE_VALUE){
		if (!GetVolumeInformationByHandleW(handle, volume_name, ARRAYSIZE(volume_name), nullptr, nullptr, nullptr, nullptr, 0))
			volume_name[0] = 0;
		CloseHandle(handle);
	}else
		volume_name[0] = 0;

	cb(volume, volume_name, GetDriveTypeW(volume));
}

EXPORT_THIS int enumerate_volumes(enumerate_volumes_callback_t cb){
	std::vector<wchar_t> buffer(64);
	HANDLE handle;
	while (true){
		handle = FindFirstVolumeW(&buffer[0], (DWORD)buffer.size());
		if (handle != INVALID_HANDLE_VALUE)
			break;

		auto error = GetLastError();
		if (error == ERROR_NO_MORE_FILES)
			return 0;
		if (error != ERROR_FILENAME_EXCED_RANGE)
			return error;
		buffer.resize(buffer.size() * 2);
	}
	while (true){
		enumerate_volumes_helper(&buffer[0], cb);

		bool done = false;
		while (!FindNextVolumeW(handle, &buffer[0], (DWORD)buffer.size())){
			auto error = GetLastError();
			if (!error || error == ERROR_NO_MORE_FILES){
				done = true;
				break;
			}
			if (error != ERROR_FILENAME_EXCED_RANGE)
				return error;
			buffer.resize(buffer.size() * 2);
		}
		if (done)
			break;
	}
	FindVolumeClose(handle);
	return 0;
}

EXPORT_THIS int enumerate_mounted_paths(const wchar_t *volume_path, string_callback_t cb){
	std::vector<wchar_t> buffer(64);
	DWORD length;
	while (true){
		auto success = GetVolumePathNamesForVolumeNameW(volume_path, &buffer[0], (DWORD)buffer.size(), &length);
		if (success)
			break;
		auto error = GetLastError();
		if (error == ERROR_MORE_DATA){
			buffer.resize(length);
			continue;
		}
		return error;
	}
	std::wstring strings(&buffer[0], length);
	size_t pos = 0;
	size_t end;
	while (pos != (end = strings.find((wchar_t)0, pos))){
		if (end == strings.npos)
			break;
		cb(strings.substr(pos, end - pos).c_str());
		pos = end + 1;
	}
	return 0;
}
