#include "stdafx.h"
#include "FileComparer.h"
#include "circular_buffer.h"
#include "RollingChecksum.h"
#include "MiscTypes.h"
#include "StreamBlockReader.h"
#include "RsyncableFile.h"
#include "binary_search.h"

void simple_buffer::realloc(size_t capacity){
	if (capacity != this->m_capacity){
		if (capacity)
			this->m_buffer.reset(new byte_t[capacity], [](byte_t *p){ delete[] p; });
		else
			this->m_buffer.reset();
		this->m_capacity = capacity;
	}
	this->size = 0;
}

byte_t *simple_buffer::data(){
	return this->m_buffer.get();
}

const byte_t *simple_buffer::data() const{
	return this->m_buffer.get();
}

void simple_buffer::operator=(const circular_buffer &buffer){
	this->realloc(buffer.size());
	this->size = 0;
	buffer.process_whole([this](const byte_t *buffer, size_t size){
		memcpy(this->m_buffer.get() + this->size, buffer, size);
		this->size += size;
	});
}

template <typename T>
int unsignedcmp(T a, T b){
	if (a < b)
		return -1;
	else if (a > b)
		return 1;
	return 0;
}

AbstractFileComparer::AbstractFileComparer(): thread(nullptr){
	this->result.reset(new std::vector<rsync_command>);
}

void AbstractFileComparer::process(){
	static const state_function functions[] = {
		&AbstractFileComparer::state_Initial,
		&AbstractFileComparer::state_Matching,
		&AbstractFileComparer::state_NonMatching,
	};
	this->state = State::Initial;
	this->started();
	while (this->state != State::Final)
		(this->*functions[(int)this->state])();
	this->finished();
}

void AbstractFileComparer::started(){
	this->state = State::Initial;
	if (this->thread_required())
		this->thread = CreateThread(nullptr, 0, static_thread_func, this, 0, nullptr);
}

void AbstractFileComparer::finished(){
	this->request_thread_stop();
	if (this->thread){
		WaitForSingleObject(this->thread, INFINITE);
		CloseHandle(this->thread);
		this->thread = nullptr;
	}
}

void AbstractFileComparer::state_Initial(){
	this->reset_state();
	this->new_offset = 0;
	this->old_offset = 0;
	if (!this->read_more_data()){
		this->state = State::Final;
		return;
	}
	this->state = this->search() ? State::Matching : State::NonMatching;
}

void AbstractFileComparer::state_Matching(){
	while (1){
		rsync_command command;
		command.file_offset = this->old_offset;
		command.length = 0;
		command.set_copy_from_old(true);
		this->result->push_back(command);

		auto last = &this->result->back();
		while (1){
			auto matching_increment = this->matching_increment();
			this->new_offset += matching_increment;
			last->length += matching_increment;
			if (!this->read_more_data()){
				this->state = State::Final;
				return;
			}
			auto target = last->file_offset + last->get_length();
			if (!this->search(true, target)){
				this->state = State::NonMatching;
				return;
			}
			if (this->old_offset != target)
				break;
		}
	}
}

void AbstractFileComparer::state_NonMatching(){
	rsync_command command;
	command.file_offset = this->new_offset;
	command.length = 0;
	command.set_copy_from_old(false);
	this->result->push_back(command);

	auto last = &this->result->back();
	do{
		auto non_matching_increment = this->non_matching_increment();
		this->new_offset += non_matching_increment;
		last->length += non_matching_increment;

		if (!this->non_matching_read_more_data()){
			this->state = State::Final;
			return;
		}
	}while (!this->search());
	this->state = State::Matching;
}

FileComparer::FileComparer(const wchar_t *new_path, std::shared_ptr<RsyncableFile> old_file):
		old_file(old_file),
		buffer(1){
	this->reader.reset(new ByteByByteReader(new_path, old_file->get_block_size()));
	auto file_size = this->reader->size();
	this->new_block_size = RsyncableFile::scaler_function(file_size);
	this->new_buffer.realloc(this->new_block_size);
	this->new_table.reserve(blocks_per_file(file_size, this->new_buffer.capacity()));
}

const byte *FileComparer::get_old_digest() const{
	return this->old_file->get_digest();
}

void FileComparer::request_thread_stop(){
	this->process_new_buffer(true);
	this->processing_queue.push_back(simple_buffer());
}

void FileComparer::reset_state(){
	this->reader->seek(0);
}

bool FileComparer::read_more_data(){
	if (!this->read_another_block(this->buffer))
		return false;
	this->checksum = compute_rsync_rolling_checksum(this->buffer);
	return true;
}

bool FileComparer::non_matching_read_more_data(){
	auto size = this->buffer.size();
	this->checksum = subtract_rsync_rolling_checksum(this->checksum, this->buffer.pop(), size);
	byte_t byte;
	if (this->read_another_byte(byte)){
		this->buffer.push(byte);
		auto n = this->buffer.size();
		this->checksum = add_rsync_rolling_checksum(this->checksum, byte, n);
	} else if (!this->buffer.size())
		return false;

	return true;
}

bool FileComparer::search(bool offset_valid, file_offset_t target_offset){
	if (this->old_file->does_not_contain(this->checksum))
		return false;
	const rsync_table_item *begin, *end,
		*begin0, *end0,
		*begin1, *end1,
		*begin2, *end2;
	this->old_file->get_table(begin, end);
	triple_search(begin0, end0, begin, end, [this](const rsync_table_item &a){
		return unsignedcmp(a.rolling_checksum, this->checksum);
	});
	if (begin0 == end0)
		return false;

	byte_t hash[20];
	{
		CryptoPP::SHA1 sha1;
		this->buffer.process_whole([&](const byte *buffer, size_t size){ sha1.Update(buffer, size); });
		sha1.Final(hash);
	}
	triple_search(begin1, end1, begin0, end0, [&hash](const rsync_table_item &a){
		return memcmp(a.complex_hash, hash, sizeof(a.complex_hash));
	});
	if (begin1 == end1)
		return false;
	while (1){
		if (!offset_valid){
			this->old_offset = begin1->file_offset;
			return true;
		}
		triple_search(begin2, end2, begin1, end1, [=](const rsync_table_item &a){
			return unsignedcmp(a.file_offset, target_offset);
		});
		if (begin2 != end2)
			begin1 = begin2;
		offset_valid = false;
	}
}

size_t FileComparer::matching_increment(){
#ifdef _DEBUG
	if (this->old_file->get_block_size() != this->buffer.size())
		__debugbreak();
#endif
	return this->old_file->get_block_size();
}

size_t FileComparer::non_matching_increment(){
	return 1;
}

bool FileComparer::read_another_byte(byte_t &dst){
	auto ret = this->reader->next_byte(dst);
	if (ret)
		this->add_byte(dst);
	return ret;
}

bool FileComparer::read_another_block(circular_buffer &buffer){
	auto ret = this->reader->whole_block(buffer);
	if (ret)
		this->add_block(buffer);
	return ret;
}

void FileComparer::add_byte(byte_t byte){
	this->new_buffer.data()[this->new_buffer.size++] = byte;
	this->process_new_buffer();
}

void FileComparer::add_block(circular_buffer &buffer){
	buffer.process_whole([this](const byte_t *buf, size_t size){ this->add_block(buf, size); });
}

void FileComparer::add_block(const byte_t *buffer, size_t size){
	while (size){
		size_t begin = this->new_buffer.size;
		auto consumed = std::min(this->new_buffer.capacity() - begin, size);
		this->new_buffer.size += consumed;
		memcpy(this->new_buffer.data() + begin, buffer, consumed);
		this->process_new_buffer();
		buffer += consumed;
		size -= consumed;
	}
}

void FileComparer::process_new_buffer(bool force){
	if (!force && !this->new_buffer.full())
		return;
	{
		AutoMutex am(this->queue_mutex);
		auto capacity = this->new_buffer.capacity();
		this->processing_queue.push_back(this->new_buffer);
		this->new_buffer = simple_buffer(capacity);
	}
	this->event.set();
}

void FileComparer::thread_func(){
	file_offset_t offset = 0;
	while (true){
		simple_buffer buffer;
		while (true){
			{
				AutoMutex am(this->queue_mutex);
				if (this->processing_queue.size()){
					buffer = this->processing_queue.front();
					this->processing_queue.pop_front();
					break;
				}
			}
			this->event.wait();
		}
		if (!buffer)
			break;

		rsync_table_item item;
		item.rolling_checksum = compute_rsync_rolling_checksum(buffer.data(), buffer.size);
		CryptoPP::SHA1 sha1;
		sha1.CalculateDigest(item.complex_hash, buffer.data(), buffer.size);
		this->new_sha1.Update(buffer.data(), buffer.size);
		item.file_offset = offset;
		this->new_table.push_back(item);
		offset += buffer.size;
	}
	this->new_sha1.Final(this->new_digest);
	std::sort(this->new_table.begin(), this->new_table.end());
}
