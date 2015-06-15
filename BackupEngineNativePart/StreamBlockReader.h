#pragma once

class circular_buffer;

class StreamBlockReader{
	HANDLE file;
	OVERLAPPED overlapped;
	std::shared_ptr<circular_buffer> next_buffer;
	bool reading;

	StreamBlockReader(const StreamBlockReader &){}
	void operator=(const StreamBlockReader &){}
	void cancel();
	void alloc();
protected:
	file_offset_t offset;
	static const size_t default_disk_block_size = 1 << 13;
	size_t disk_block_size;
	bool eof;

	void read_more();
	virtual void clear_buffers() = 0;
	std::shared_ptr<circular_buffer> finish_read();
public:
	StreamBlockReader(const wchar_t *path, size_t block_size = default_disk_block_size);
	virtual ~StreamBlockReader();
	void seek(file_offset_t offset);
	bool at_eof() const{
		return this->eof;
	}
	file_size_t size(); 
};

class BlockByBlockReader : public StreamBlockReader{
protected:
	std::shared_ptr<circular_buffer> current_buffer;
	size_t block_size;

	virtual void clear_buffers();
public:
	BlockByBlockReader(const wchar_t *path, size_t block_size = 0);
	virtual ~BlockByBlockReader(){}
	bool next_block(circular_buffer &);
};

class ByteByByteReader : private BlockByBlockReader{
	std::shared_ptr<circular_buffer> current_buffer2;

	void clear_buffers();
	bool read_more2();
public:
	ByteByByteReader(const wchar_t *path, size_t block_size = 0);
	bool next_byte(byte_t &);
	bool whole_block(circular_buffer &);
	void seek(file_offset_t offset){
		BlockByBlockReader::seek(offset);
	}
	file_size_t size(){
		return StreamBlockReader::size();
	}
};
