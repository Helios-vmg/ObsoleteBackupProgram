#pragma once

#include "circular_buffer.h"
#include "Threads.h"
class ByteByByteReader;
class RsyncableFile;
struct rsync_command;
struct rsync_table_item;

class simple_buffer{
	std::shared_ptr<byte_t> m_buffer;
	size_t m_capacity;
public:
	size_t size;
	simple_buffer(size_t capacity = 0): m_capacity(0){
		this->realloc(capacity);
	}
	void realloc(size_t capacity = 0);
	byte_t *data();
	const byte_t *data() const;
	operator bool(){
		return (bool)this->m_buffer;
	}
	size_t capacity() const{
		return this->m_capacity;
	}
	bool full() const{
		return this->size == this->m_capacity;
	}
	void operator=(const circular_buffer &);
};

class FileComparer{
	enum class State{
		Initial = 0,
		Matching,
		NonMatching,
		Final,
	} state;
	std::shared_ptr<std::vector<rsync_command> > result;
	std::shared_ptr<ByteByByteReader> reader;
	std::shared_ptr<RsyncableFile> old_file;
	circular_buffer buffer;
	rolling_checksum_t checksum;
	file_offset_t new_offset,
		old_offset;
	file_size_t new_block_size;
	CryptoPP::SHA1 new_sha1;
	byte_t new_digest[20];
	simple_buffer new_buffer;
	std::vector<rsync_table_item> new_table;
	HANDLE thread;
	Mutex queue_mutex;
	AutoResetEvent event;
	std::deque<simple_buffer> processing_queue;
	bool stop;

	typedef void (FileComparer::*state_function)();
	void state_Initial();
	void state_Matching();
	void state_NonMatching();
	bool search(bool offset_valid = false, file_offset_t target_offset = 0);
	bool read_another_byte(byte_t &);
	bool read_another_block(circular_buffer &);
	void add_byte(byte_t);
	void add_block(circular_buffer &);
	void add_block(const byte_t *, size_t);
	void process_new_buffer(bool force = false);
	void started();
	void finished();
	static DWORD WINAPI static_thread_func(void *_this){
		((FileComparer *)_this)->thread_func();
		return 0;
	}
	void thread_func();
public:
	FileComparer(const wchar_t *new_path, std::shared_ptr<RsyncableFile> old_file);
	void process();
	std::shared_ptr<std::vector<rsync_command> > get_result(){
		auto ret = this->result;
		this->result.reset();
		return ret;
	}
	std::vector<rsync_table_item> get_new_table() const{
		return this->new_table;
	}
	const byte *get_old_digest() const;
	const byte *get_new_digest() const{
		return this->new_digest;
	}
	file_size_t get_new_block_size() const{
		return this->new_block_size;
	}
};

