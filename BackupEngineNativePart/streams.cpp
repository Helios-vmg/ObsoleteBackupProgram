#include "stdafx.h"
#include "streams.h"
#include "MiscFunctions.h"
#include "MiscTypes.h"
#include "ExportedFunctions.h"

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

DotNetInputStream::DotNetInputStream(read_callback_t read, eof_callback_t eof, release_callback_t release){
	this->read_callback = read;
	this->eof_callback = eof;
	this->release_callback = release;
}

DotNetInputStream::~DotNetInputStream(){
	if (this->release_callback)
		this->release_callback();
}

size_t DotNetInputStream::read(void *_buffer, size_t size){
	auto buffer = (std::uint8_t *)_buffer;
	const int max = std::numeric_limits<int>::max();
	size_t ret = 0;
	while (size && !this->eof_callback()){
		int read_size = size > max ? max : (int)size;
		read_size = this->read_callback(buffer, read_size);
		buffer += read_size;
		size -= read_size;
		ret += read_size;
	}
	return ret;
}

bool DotNetInputStream::eof(){
	return this->eof_callback();
}

DotNetOutputStream::DotNetOutputStream(write_callback_t write, flush_callback_t flush, release_callback_t release){
	this->write_callback = write;
	this->flush_callback = flush;
	this->release_callback = release;
}

DotNetOutputStream::~DotNetOutputStream(){
	if (this->release_callback)
		this->release_callback();
}

void DotNetOutputStream::write(const void *_buffer, size_t size){
	auto buffer = (const std::uint8_t *)_buffer;
	const int max = std::numeric_limits<int>::max();
	size_t ret = 0;
	while (size){
		int write_size = size > max ? max : (int)size;
		this->write_callback(buffer, write_size);
		buffer += write_size;
		size -= write_size;
		ret += write_size;
	}
}

void DotNetOutputStream::flush(){
	this->flush_callback();
}

EXPORT_THIS void *encapsulate_dot_net_input_stream(DotNetInputStream::read_callback_t read, DotNetInputStream::eof_callback_t eof, DotNetInputStream::release_callback_t release){
	return new std::shared_ptr<InStream>(new DotNetInputStream(read, eof, release));
}

EXPORT_THIS void *encapsulate_dot_net_output_stream(DotNetOutputStream::write_callback_t w, DotNetOutputStream::flush_callback_t f, DotNetOutputStream::release_callback_t r){
	return new std::shared_ptr<OutStream>(new DotNetOutputStream(w, f, r));
}

EXPORT_THIS void release_input_stream(void *p){
	delete (std::shared_ptr<InStream> *)p;
}

EXPORT_THIS void release_output_stream(void *p){
	delete (std::shared_ptr<OutStream> *)p;
}

EXPORT_THIS int read_from_input_stream(void *p, std::uint8_t *buffer, int offset, int length){
	auto stream = (std::shared_ptr<InStream> *)p;
	return (*stream)->read(buffer + offset, length);
}

EXPORT_THIS void write_to_output_stream(void *p, std::uint8_t *buffer, int offset, int length){
	auto stream = (std::shared_ptr<OutStream> *)p;
	(*stream)->write(buffer + offset, length);
}

EXPORT_THIS void flush_output_stream(void *p){
	auto stream = (std::shared_ptr<OutStream> *)p;
	(*stream)->flush();
}
