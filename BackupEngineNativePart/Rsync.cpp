#include "stdafx.h"
#include "Rsync.h"
#include "StreamBlockReader.h"
#include "circular_buffer.h"
#include "MiscTypes.h"
#include "MiscFunctions.h"

rsync::NormalFile::NormalFile(const wchar_t *path, u64 unique_id): unique_id(unique_id){
	this->reader.reset(new BlockByBlockReader(path));
}

void rsync::NormalFile::reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size){
	reconstructed_part part = {
		this->unique_id,
		virtual_offset,
		size,
	};
	dst.push_back(part);
}

bool rsync::NormalFile::seek(file_offset_t offset){
	try{
		this->reader->seek(offset);
	}catch (Win32Error &){
		return false;
	}
	this->temp.size = 0;
	return true;
}

bool rsync::NormalFile::read(void *dst, size_t size, size_t &bytes_read){
	bytes_read = 0;
	while (size){
		if (this->temp.size){
			auto consumed = std::min(this->temp.size, size);
			memcpy(dst, this->temp.data() + this->temp.capacity() - this->temp.size, consumed);
			dst = (char *)dst + consumed;
			size -= consumed;
			this->temp.size -= consumed;
			bytes_read += consumed;
			continue;
		}

		circular_buffer buffer(1);
		try{
			if (!this->reader->next_block(buffer))
				break;
		}catch (Win32Error &){
			return false;
		}
		this->temp = buffer;
	}
	return true;
}

bool rsync::NormalFile::eof(){
	return this->reader->at_eof();
}

template <typename T>
bool part_order(const T &a, const T &b){
	return a.virtual_offset < b.virtual_offset;
}

rsync::SparseFile::SparseFile(const wchar_t *path, u64 unique_id, rsync_command *table, size_t table_size):
		NormalFile(path, unique_id){
	this->parts.reserve(table_size);
	file_offset_t offset = 0;
	for (size_t i = 0; i < table_size; i++){
		auto item = table[i];
		if (item.copy_from_old()){
			part p = {
				offset,
				item.file_offset,
				item.get_length(),
			};
			this->parts.push_back(p);
		}
		offset += item.length;
	}
	std::sort(this->parts.begin(), this->parts.end(), part_order<part>);
	this->current = 0;
	if (this->parts.size())
		this->offset = this->parts.front().virtual_offset;
}

template <typename Iterator>
Iterator find_part(Iterator begin, Iterator end, file_offset_t offset){
	return find_all(
		begin,
		end,
		[offset](const decltype(*begin) &p){
			return p.virtual_offset <= offset && offset < p.virtual_offset + p.size || p.virtual_offset >= offset;
		}
	);
}

bool rsync::SparseFile::seek(file_offset_t offset){
	if (!this->transform_offset(this->current, offset))
		return false;
	auto ret = NormalFile::seek(offset);
	if (ret)
		this->offset = offset;
	return ret;
}

bool rsync::SparseFile::transform_offset(size_t &containing_part, file_offset_t &offset){
	auto it = find_part(this->parts.begin(), this->parts.end(), offset);
	if (it == this->parts.end() || offset >= it->virtual_offset + it->size)
		return false;
	containing_part = it - this->parts.end();
	offset = it->physical_offset + (offset - it->virtual_offset);
	return true;
}

void rsync::SparseFile::reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size){
	while (size){
		size_t containing_part;
		file_offset_t physical_offset = virtual_offset;
		if (!this->transform_offset(containing_part, physical_offset))
			break;
		auto *part = &this->parts[containing_part];
		auto consumed = std::min(size, part->size - (virtual_offset - part->virtual_offset));
		reconstructed_part new_part = {
			this->unique_id,
			physical_offset,
			consumed,
		};
		dst.push_back(new_part);
		virtual_offset += consumed;
		size -= consumed;
	}
}

bool rsync::SparseFile::read(void *dst, size_t size, size_t &bytes_read){
	bytes_read = 0;
	while (size){
		auto *part = &this->parts[this->current];
		auto consumed = std::min(size, part->size - (this->offset - part->virtual_offset));
		size_t temp;
		if (!NormalFile::read(dst, consumed, temp))
			return false;
		consumed = temp;
		dst = (char *)dst + consumed;
		bytes_read += consumed;
		size -= consumed;
		this->offset += consumed;
		assert(this->offset <= part->virtual_offset + part->size);
		if (this->offset == part->virtual_offset + part->size){
			this->current++;
			if (this->eof())
				break;
			part = &this->parts[this->current];
			if (!this->seek(part->virtual_offset))
				return false;
			this->offset = part->virtual_offset;
		}
	}
	return true;
}

bool rsync::SparseFile::eof(){
	return this->current >= this->parts.size();
}

rsync::RsyncChainLink::RsyncChainLink(std::shared_ptr<Stream> old_file, std::shared_ptr<Stream> new_file, rsync_command *table, size_t table_size):
		old_file(old_file),
		new_file(new_file){
	this->parts.reserve(table_size);
	file_offset_t offset = 0;
	for (size_t i = 0; i < table_size; i++){
		auto item = table[i];
		part p = {
			item.file_offset,
			offset,
			item.get_length(),
			!item.copy_from_old(),
		};
		this->parts.push_back(p);
		offset += item.get_length();
	}
	std::sort(this->parts.begin(), this->parts.end(), part_order<part>);
	this->current = 0;
	this->offset = 0;
}

bool rsync::RsyncChainLink::seek(file_offset_t offset){
	size_t containing_part;
	if (this->transform_offset(containing_part, offset))
		return false;
	this->current = containing_part;
	auto stream = !this->parts[this->current].file ? this->old_file : this->new_file;
	auto ret = stream->seek(offset);
	if (ret)
		this->offset = offset;
	return ret;
}

bool rsync::RsyncChainLink::transform_offset(size_t &containing_part, file_offset_t &offset){
	auto it = find_part(this->parts.begin(), this->parts.end(), offset);
	if (it == this->parts.end() || offset >= it->virtual_offset + it->size)
		return false;
	containing_part = it - this->parts.begin();
	auto stream = !it->file ? this->old_file : this->new_file;
	offset = it->physical_offset + (offset - it->virtual_offset);
	return true;
}

void rsync::RsyncChainLink::reconstruct_section(std::vector<reconstructed_part> &dst, file_offset_t virtual_offset, file_size_t size){
	while (size){
		size_t containing_part;
		file_offset_t physical_offset = virtual_offset;
		if (!this->transform_offset(containing_part, physical_offset))
			break;
		auto *part = &this->parts[containing_part];
		auto consumed = std::min(size, part->size - (virtual_offset - part->virtual_offset));
		auto stream = !part->file ? this->old_file : this->new_file;
		stream->reconstruct_section(dst, physical_offset, consumed);
		virtual_offset += consumed;
		size -= consumed;
	}
}

bool rsync::RsyncChainLink::read(void *dst, size_t size, size_t &bytes_read){
	bytes_read = 0;
	while (size){
		auto *part = &this->parts[this->current];
		auto consumed = std::min(size, part->size - (this->offset - part->virtual_offset));
		size_t temp;
		auto stream = !part->file ? this->old_file : this->new_file;
		if (!stream->read(dst, consumed, temp))
			return false;
		consumed = temp;
		dst = (char *)dst + consumed;
		bytes_read += consumed;
		size -= consumed;
		this->offset += consumed;
		assert(this->offset <= part->virtual_offset + part->size);
		if (this->offset == part->virtual_offset + part->size){
			this->current++;
			if (this->eof())
				break;
			part = &this->parts[this->current];
			if (!this->seek(part->virtual_offset))
				return false;
			this->offset = part->virtual_offset;
		}
	}
	return true;
}

bool rsync::RsyncChainLink::eof(){
	return this->current >= this->parts.size();
};
