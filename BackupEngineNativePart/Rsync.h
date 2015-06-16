#pragma once

#include "FileComparer.h"
#include "StreamBlockReader.h"
struct rsync_command;

namespace rsync{

struct reconstructed_part{
	u64 source;
	file_offset_t offset_in_source;
	file_size_t size;
};

class Stream{
public:
	virtual ~Stream(){}
	virtual bool seek(file_offset_t) = 0;
	virtual bool read(void *dst, size_t size, size_t &bytes_read) = 0;
	virtual bool eof() = 0;
	virtual u64 get_version() const = 0;
	virtual u64 get_unique_id() const = 0;
	virtual void reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size) = 0;
};

class NormalFile : public Stream{
protected:
	std::unique_ptr<BlockByBlockReader> reader;
	simple_buffer temp;
	u64 unique_id;
public:
	NormalFile(const wchar_t *path, u64 unique_id);
	virtual ~NormalFile(){}
	virtual bool seek(file_offset_t) override;
	virtual bool read(void *dst, size_t size, size_t &bytes_read) override;
	virtual bool eof() override;
	virtual u64 get_version() const override{
		return 0;
	}
	virtual u64 get_unique_id() const override{
		return this->unique_id;
	}
	virtual void reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size) override;
};

class SparseFile : public NormalFile{
public:
	struct part{
		file_offset_t physical_offset;
		file_offset_t virtual_offset;
		file_size_t size;
	};
protected:
	std::vector<part> parts;
	size_t current;
	file_offset_t offset;
	
	bool transform_offset(size_t &containing_part, file_offset_t &offset);
public:
	SparseFile(const wchar_t *path, u64 unique_id, rsync_command *table, size_t table_size);
	virtual ~SparseFile(){}
	bool seek(file_offset_t) override;
	bool read(void *dst, size_t size, size_t &bytes_read) override;
	std::vector<part> list_parts() const{
		return this->parts;
	}
	bool eof() override;
	virtual void reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size) override;
};

class RsyncChainLink : public Stream{
public:
	struct part{
		file_offset_t physical_offset;
		file_offset_t virtual_offset;
		file_size_t size;
		u64 file;
	};
protected:
	std::shared_ptr<Stream> old_file,
		new_file;
	std::vector<part> parts;
	size_t current;
	file_offset_t offset;

	bool transform_offset(size_t &containing_part, file_offset_t &offset);
public:
	RsyncChainLink(std::shared_ptr<Stream> old_file, std::shared_ptr<Stream> new_file, rsync_command *table, size_t table_size);
	bool seek(file_offset_t) override;
	bool read(void *dst, size_t size, size_t &bytes_read) override;
	bool eof() override;
	u64 get_version() const override{
		return this->old_file->get_version() + 1;
	}
	u64 get_unique_id() const override{
		return ~(u64)0;
	}
	void reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size) override;
};

}
