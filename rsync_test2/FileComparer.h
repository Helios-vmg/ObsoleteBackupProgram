#pragma once

#include "circular_buffer.h"
class ByteByByteReader;
class RsyncableFile;
struct rsync_command;
struct rsync_table_item;

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
	CryptoPP::SHA1 new_sha1;
	byte_t new_digest[20];
	size_t new_buffer_size,
		new_buffer_capacity;
	std::unique_ptr<byte_t[]> new_buffer;
	std::vector<rsync_table_item> new_table;

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
	void finished();
public:
	FileComparer(const wchar_t *new_path, std::shared_ptr<RsyncableFile> old_file);
	void process();
	std::shared_ptr<std::vector<rsync_command> > get_result(){
		auto ret = this->result;
		this->result.reset();
		return ret;
	}
	const byte *get_old_digest() const;
	const byte *get_new_digest(){
		return this->new_digest;
	}
};

