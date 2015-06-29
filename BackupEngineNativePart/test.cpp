#include "stdafx.h"
#include "Rsync.h"
#include "FileComparer.h"
#include "RsyncableFile.h"
#include "ExportedFunctions.h"
#include "MiscFunctions.h"
#include "Rdiff.h"

int internal_get_reparse_point_target(const wchar_t *path, unsigned long *unrecognized, std::wstring *target_path, bool *is_symlink);

EXPORT_THIS void test_func(){
	unsigned long unrecognized;
	std::wstring result;
	bool is_symlink;
	auto ret = internal_get_reparse_point_target(L"f:/Backups/test/README2.txt", &unrecognized, &result, &is_symlink);
	std::wcout << result << std::endl;
	ret = create_symlink(L"f:/Backups/test/README4.txt", result.c_str());
	//ret = internal_get_reparse_point_target(L"f:/Backups/test/README3.txt", &unrecognized, &result, &is_symlink);
	//std::wcout << result << std::endl;
	return;
}
