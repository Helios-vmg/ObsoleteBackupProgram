#include "stdafx.h"
#include "streams.h"
#include "MiscFunctions.h"
#include "MiscTypes.h"

FileOutputStream::FileOutputStream(const wchar_t *_path){
	auto path = path_from_string(_path);
	this->file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
	if (this->file == INVALID_HANDLE_VALUE)
		throw Win32Error();
}

FileOutputStream::~FileOutputStream(){
	if (this->file && this->file != INVALID_HANDLE_VALUE)
		CloseHandle(this->file);
}

void FileOutputStream::write(const void *buffer, size_t size){
	while (size){
		DWORD bytes_written;
		auto success = WriteFile(this->file, buffer, size & 0xFFFFFFFF, &bytes_written, nullptr);
		if (!success)
			throw Win32Error();
		if (bytes_written > size)
			// Huh?
			size = 0;
		else
			size -= bytes_written;
		buffer = (char *)buffer + bytes_written;
	}
}

void FileOutputStream::flush(){
	FlushFileBuffers(this->file);
}

FileInputStream::FileInputStream(const wchar_t *_path){
	auto path = path_from_string(_path);
	this->file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
	if (this->file == INVALID_HANDLE_VALUE)
		throw Win32Error();
}

FileInputStream::~FileInputStream(){
	if (this->file && this->file != INVALID_HANDLE_VALUE)
		CloseHandle(this->file);
}

size_t FileInputStream::read(void *buffer, size_t size){
	size_t ret = 0;
	while (size){
		DWORD bytes_read;
		auto success = ReadFile(this->file, buffer, size & 0xFFFFFFFF, &bytes_read, nullptr);
		if (!success)
			throw Win32Error();
		if (bytes_read > size)
			// Huh?
			size = 0;
		else
			size -= bytes_read;
		buffer = (char *)buffer + bytes_read;
		ret += bytes_read;
	}
	return ret;
}

bool FileInputStream::eof(){
	LARGE_INTEGER li, size;
	li.QuadPart = 0;
	if (!SetFilePointerEx(this->file, li, &li, FILE_CURRENT))
		throw Win32Error();
	GetFileSizeEx(this->file, &size);
	return li.QuadPart >= size.QuadPart;
}
