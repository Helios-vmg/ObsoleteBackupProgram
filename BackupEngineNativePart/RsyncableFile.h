#pragma once
#include "MiscTypes.h"

class FileComparer;

class RsyncableFile{
	byte_t sha1[20];
	std::vector<rsync_table_item> rsync_table;
	std::vector<bool> bitmap;
	file_size_t block_size;

	void init_bitmap();
public:
	RsyncableFile(const std::wstring &path);
	RsyncableFile(const FileComparer &);
	void save(const char *path);
	static file_size_t scaler_function(file_size_t x){
		const size_t limit = 64 << 20;
		u64 ret = 512;
		while ((x + (ret - 1)) / ret * sizeof(rsync_table_item) > limit)
			ret <<= 1;
		return ret;
	}
	file_size_t get_block_size() const{
		return this->block_size;
	}
	void get_table(const rsync_table_item *&begin, const rsync_table_item *&end) const{
		if (!this->rsync_table.size()){
			begin = nullptr;
			end = nullptr;
			return;
		}
		begin = &this->rsync_table[0];
		end = begin + this->rsync_table.size();
	}
	const byte *get_digest() const{
		return this->sha1;
	}
	bool does_not_contain(rolling_checksum_t);
};

inline size_t blocks_per_file(file_size_t file_size, size_t block_size){
	return (file_size + (block_size - 1)) / block_size;
}
