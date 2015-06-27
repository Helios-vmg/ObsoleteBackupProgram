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

class AbstractFileComparer{
protected:
	enum class State{
		Initial = 0,
		Matching,
		NonMatching,
		Final,
	} state;
private:
	std::shared_ptr<std::vector<rsync_command> > result;
	file_offset_t new_offset;
	HANDLE thread;

	typedef void (AbstractFileComparer::*state_function)();
	void state_Initial();
	void state_Matching();
	void state_NonMatching();
	void started();
	void finished();
	static DWORD WINAPI static_thread_func(void *_this){
		((AbstractFileComparer *)_this)->thread_func();
		return 0;
	}
protected:
	file_offset_t old_offset;
	AutoResetEvent event;

	State get_state() const{
		return this->state;
	}
	virtual void reset_state(){}
	virtual bool read_more_data() = 0;
	virtual bool non_matching_read_more_data() = 0;
	virtual size_t matching_increment() = 0;
	virtual size_t non_matching_increment() = 0;
	virtual bool search(bool offset_valid = false, file_offset_t target_offset = 0) = 0;
	virtual void thread_func() = 0;
	virtual void request_thread_stop() = 0;
	virtual bool thread_required(){
		return false;
	}
public:
	AbstractFileComparer();
	virtual ~AbstractFileComparer(){}
	void process();
	std::shared_ptr<std::vector<rsync_command> > get_result(){
		auto ret = this->result;
		this->result.reset();
		return ret;
	}
};

class FileComparer : public AbstractFileComparer{
	std::shared_ptr<ByteByByteReader> reader;
	std::shared_ptr<RsyncableFile> old_file;
	circular_buffer buffer;
	rolling_checksum_t checksum;
	file_size_t new_block_size;
	CryptoPP::SHA1 new_sha1;
	byte_t new_digest[20];
	simple_buffer new_buffer;
	std::vector<rsync_table_item> new_table;
	Mutex queue_mutex;
	std::deque<simple_buffer> processing_queue;
	
	bool read_another_byte(byte_t &);
	bool read_another_block(circular_buffer &);
	void add_byte(byte_t);
	void add_block(circular_buffer &);
	void add_block(const byte_t *, size_t);
	void process_new_buffer(bool force = false);
protected:
	void reset_state() override;
	bool read_more_data() override;
	bool non_matching_read_more_data() override;
	size_t matching_increment() override;
	size_t non_matching_increment() override;
	bool search(bool offset_valid = false, file_offset_t target_offset = 0) override;
	void thread_func() override;
	void request_thread_stop() override;
	bool thread_required() override{
		return true;
	}
public:
	FileComparer(const wchar_t *new_path, std::shared_ptr<RsyncableFile> old_file);
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

