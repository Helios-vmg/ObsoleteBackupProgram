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
		cb(&buffer[0], GetDriveTypeW(&buffer[0]));
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
