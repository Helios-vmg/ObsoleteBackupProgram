#include "stdafx.h"
#include "StreamBlockReader.h"
#include "MiscFunctions.h"
#include "MiscTypes.h"
#include "circular_buffer.h"

StreamBlockReader::StreamBlockReader(const wchar_t *_path, size_t block_size):
		eof(false),
		reading(false),
		offset(0),
		disk_block_size(block_size){
	zero_struct(this->overlapped);
	auto path = path_from_string(_path);
	this->file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
	if (!valid_handle(this->file))
		throw Win32Error();
	this->alloc();
	this->read_more();
}

void StreamBlockReader::alloc(){
	this->next_buffer.reset(new circular_buffer(this->disk_block_size));
}

StreamBlockReader::~StreamBlockReader(){
	if (valid_handle(this->file))
		CloseHandle(this->file);
}

void StreamBlockReader::seek(file_offset_t offset){
	this->cancel();
	this->clear_buffers();
	this->eof = false;
	this->offset = offset;
	LARGE_INTEGER li;
	li.QuadPart = this->offset;
	if (!SetFilePointerEx(this->file, li, nullptr, FILE_BEGIN))
		throw Win32Error();
	this->read_more();
}

file_size_t StreamBlockReader::size(){
	LARGE_INTEGER li;
	return GetFileSizeEx(this->file, &li) ? li.QuadPart : 0;
}

void StreamBlockReader::read_more(){
	if (this->eof)
		return;
	if (this->reading)
		this->cancel();
	this->next_buffer->reset();
	this->overlapped.Offset = this->offset & mask_32bits;
	this->overlapped.OffsetHigh = this->offset >> 32;
	ReadFile(this->file, this->next_buffer->data(), (DWORD)this->disk_block_size, nullptr, &overlapped);
	this->reading = true;
}

void StreamBlockReader::cancel(){
	CancelIo(this->file);
	this->reading = false;
}

std::shared_ptr<circular_buffer> StreamBlockReader::finish_read(){
	DWORD bytes_read;
	auto res = GetOverlappedResult(this->file, &this->overlapped, &bytes_read, true);
	this->reading = false;
	if (!res){
		auto error = GetLastError();
		if (error == ERROR_HANDLE_EOF){
			this->eof = true;
			return std::shared_ptr<circular_buffer>();
		}
		throw Win32Error(error);
	}
	this->offset += bytes_read;
	auto ret = this->next_buffer;
	ret->trim(bytes_read);
	this->alloc();
	this->read_more();
	return ret;
}

BlockByBlockReader::BlockByBlockReader(const wchar_t *path, size_t block_size):
		StreamBlockReader(path){
	this->block_size = !block_size ? StreamBlockReader::disk_block_size : block_size;
}

void BlockByBlockReader::clear_buffers(){
	this->current_buffer.reset();
}

bool BlockByBlockReader::next_block(circular_buffer &dst){
	if (this->eof)
		return false;
	dst.realloc(this->block_size);
	dst.reset_size();
	bool read = false;
	while (dst.size() < this->block_size){
		if (this->current_buffer && this->current_buffer->size()){
			this->current_buffer->pop_buffer(dst);
			read = false;
		}else{
			if (!read){
				this->current_buffer = this->finish_read();
				if (this->current_buffer && !this->current_buffer->data())
					__debugbreak();
				read = true;
			}else{
				this->eof = true;
				break;
			}
		}
	}
	return !!dst.size();
}

void ByteByByteReader::clear_buffers(){
	BlockByBlockReader::clear_buffers();
	this->current_buffer2.reset(new circular_buffer(1));
	this->current_buffer2->reset_size();
}

ByteByByteReader::ByteByByteReader(const wchar_t *path, size_t block_size): BlockByBlockReader(path, block_size){
	this->current_buffer2.reset(new circular_buffer(1));
	this->next_block(*this->current_buffer2);
}

bool ByteByByteReader::next_byte(byte_t &dst){
	if (!this->current_buffer2->size())
		return false;
	dst = this->current_buffer2->pop();
	if (!this->current_buffer2->size())
		this->read_more2();
	return true;
}

bool ByteByByteReader::read_more2(){
	this->next_block(*this->current_buffer2);
	return !!this->current_buffer2->size();
}

bool ByteByByteReader::whole_block(circular_buffer &dst){
	if (!this->current_buffer2->size())
		this->read_more2();
	if (!this->current_buffer2->size())
		return false;
	dst.realloc(this->block_size);
	dst.reset_size();
	this->current_buffer2->pop_buffer(dst);
	if (dst.size() != dst.capacity()){
		if (!this->read_more2())
			return true;
		this->current_buffer2->pop_buffer(dst);
	}
	if (!this->current_buffer2->size())
		this->read_more2();
	return true;
}
