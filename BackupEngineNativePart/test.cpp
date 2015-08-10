#include "stdafx.h"
#include "Rsync.h"
#include "FileComparer.h"
#include "RsyncableFile.h"
#include "ExportedFunctions.h"
#include "MiscFunctions.h"
#include "Rdiff.h"

int internal_get_reparse_point_target(const wchar_t *path, unsigned long *unrecognized, std::wstring *target_path, bool *is_symlink);

#pragma warning(push)
#pragma warning(disable:4200)
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
#pragma warning(pop)

#include <stdio.h>
#pragma comment(lib, "advapi32.lib")

BOOL SetPrivilege(
	HANDLE hToken,          // access token handle
	LPCTSTR lpszPrivilege,  // name of privilege to enable/disable
	BOOL bEnablePrivilege   // to enable or disable privilege
	)
{
	TOKEN_PRIVILEGES tp;
	LUID luid;

	if (!LookupPrivilegeValue(
		NULL,            // lookup privilege on local system
		lpszPrivilege,   // privilege to lookup 
		&luid))        // receives LUID of privilege
	{
		printf("LookupPrivilegeValue error: %u\n", GetLastError());
		return FALSE;
	}

	tp.PrivilegeCount = 1;
	tp.Privileges[0].Luid = luid;
	if (bEnablePrivilege)
		tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
	else
		tp.Privileges[0].Attributes = 0;

	// Enable the privilege or disable all privileges.

	if (!AdjustTokenPrivileges(
		hToken,
		FALSE,
		&tp,
		sizeof(TOKEN_PRIVILEGES),
		(PTOKEN_PRIVILEGES)NULL,
		(PDWORD)NULL))
	{
		printf("AdjustTokenPrivileges error: %u\n", GetLastError());
		return FALSE;
	}

	if (GetLastError() == ERROR_NOT_ALL_ASSIGNED)

	{
		printf("The token does not have the specified privilege. \n");
		return FALSE;
	}

	return TRUE;
}

HANDLE CurrentProcessToken(){
	auto proc = GetCurrentProcess();
	HANDLE ret = nullptr;
	OpenProcessToken(proc, TOKEN_ADJUST_PRIVILEGES, &ret);
	CloseHandle(proc);
	return ret;
}

// Returns directory handle or INVALID_HANDLE_VALUE if failed to open.
// To get extended error information, call GetLastError.

HANDLE OpenDirectory(LPCTSTR pszPath, BOOL bReadWrite) {
	// Obtain backup/restore privilege in case we don't have it
	HANDLE hToken;
	TOKEN_PRIVILEGES tp;
	::OpenProcessToken(::GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES, &hToken);
	::LookupPrivilegeValue(NULL,
						   (bReadWrite ? SE_RESTORE_NAME : SE_BACKUP_NAME),
						   &tp.Privileges[0].Luid);
	tp.PrivilegeCount = 1;
	tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
	::AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(TOKEN_PRIVILEGES), NULL, NULL);
	::CloseHandle(hToken);

	// Open the directory
	DWORD dwAccess = bReadWrite ? (GENERIC_READ | GENERIC_WRITE) : GENERIC_READ;
	HANDLE hDir = ::CreateFile(pszPath, dwAccess, 0, NULL, OPEN_EXISTING,
							   FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, NULL);

	return hDir;
}

#define REPARSE_MOUNTPOINT_HEADER_SIZE   8

EXPORT_THIS void test_func(const wchar_t *path){
#if 1
	{
		std::cout << "CurrentProcessToken()\n";
		auto token = CurrentProcessToken();
		if (!token)
			return;
		std::cout << "SetPrivilege()\n";
		if (!SetPrivilege(token, SE_RESTORE_NAME, true)){
			CloseHandle(token);
			return;
		}
		CloseHandle(token);
	}

	HANDLE h = CreateFileW(L"test.link", GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_DIRECTORY | FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, nullptr);
	if (h == INVALID_HANDLE_VALUE)
		return;
	std::wstring target = L"\\??\\f:\\Data\\Programming\\Visual Studio 2013\\Projects\\BackupEngine\\bin64\\test";

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
	auto status = DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, &buffer[0], (DWORD)buffer.size(), nullptr, 0, nullptr, nullptr);
	DWORD error;
	if (!status)
		error = GetLastError();
	CloseHandle(h);
	return;
#else

	//std::wstring target;
	//internal_get_reparse_point_target(L"test.link", nullptr, &target, nullptr);

	const wchar_t *szJunction = L"test.link";
	const char *szTarget = "\\??\\f:\\Data\\Programming\\Visual Studio 2013\\Projects\\BackupEngine\\bin64\\test";

	// Open for reading and writing (see OpenDirectory definition above)
	HANDLE hDir = OpenDirectory(szJunction, TRUE);


	// Take note that buf and ReparseBuffer occupy the same space
	BYTE buf[sizeof(REPARSE_MOUNTPOINT_DATA_BUFFER) + MAX_PATH * sizeof(WCHAR)];
	REPARSE_MOUNTPOINT_DATA_BUFFER& ReparseBuffer = (REPARSE_MOUNTPOINT_DATA_BUFFER&)buf;

	// Prepare reparse point data
	memset(buf, 0, sizeof(buf));
	ReparseBuffer.ReparseTag = IO_REPARSE_TAG_MOUNT_POINT;
	int len = ::MultiByteToWideChar(CP_ACP, 0, szTarget, -1,
									ReparseBuffer.ReparseTarget, MAX_PATH);
	ReparseBuffer.ReparseTargetMaximumLength = (len--) * sizeof(WCHAR);
	ReparseBuffer.ReparseTargetLength = len * sizeof(WCHAR);
	ReparseBuffer.ReparseDataLength = ReparseBuffer.ReparseTargetLength + 12;

	// Attach reparse point
	DWORD dwRet;
	::DeviceIoControl(hDir, FSCTL_SET_REPARSE_POINT, &ReparseBuffer,
					  ReparseBuffer.ReparseDataLength + REPARSE_MOUNTPOINT_HEADER_SIZE,
					  NULL, 0, &dwRet, NULL);

#endif
}
