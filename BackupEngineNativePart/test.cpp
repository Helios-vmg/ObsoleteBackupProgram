#include "stdafx.h"
#include "Rsync.h"
#include "FileComparer.h"
#include "RsyncableFile.h"
#include "ExportedFunctions.h"
#include "MiscFunctions.h"
#include "Rdiff.h"

int internal_get_reparse_point_target(const wchar_t *path, unsigned long *unrecognized, std::wstring *target_path, bool *is_symlink);

EXPORT_THIS void test_func(const wchar_t *path){
	WIN32_FIND_DATAW data;
	zero_struct(data);
	auto handle = FindFirstFileW(path, &data);
	if (handle == INVALID_HANDLE_VALUE || !handle)
		return;
	do
		std::wcout << data.cFileName << std::endl;
	while (FindNextFileW(handle, &data));
	FindClose(handle);
	return;
}
