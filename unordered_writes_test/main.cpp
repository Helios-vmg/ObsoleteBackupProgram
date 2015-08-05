#include <iostream>
#include <Windows.h>

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

int main(){
	{
		std::cout << "CurrentProcessToken()\n";
		auto token = CurrentProcessToken();
		if (!token)
			return -1;
		std::cout << "SetPrivilege()\n";
		if (!SetPrivilege(token, SE_MANAGE_VOLUME_NAME, true))
			return -1;
	}

	std::cout << "CreateFileA()\n";
	auto handle = CreateFileA("test.bin", GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
	if (handle == INVALID_HANDLE_VALUE)
		return -1;
	LARGE_INTEGER li;
	li.QuadPart = 1ULL << 33;
	SetFilePointerEx(handle, li, &li, FILE_BEGIN);
	//SetFilePointer(handle, li.LowPart, &li.HighPart, FILE_BEGIN);
	SetEndOfFile(handle);
	auto success = SetFileValidData(handle, li.QuadPart);
	if (!success){
		std::cerr << GetLastError() << std::endl;
	}
	CloseHandle(handle);
	std::cout << "OK!\n";
	return 0;
}
