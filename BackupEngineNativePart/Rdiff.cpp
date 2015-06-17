#include "stdafx.h"
#include "Rdiff.h"
#include "StreamBlockReader.h"
#include "MiscTypes.h"
#include "FileComparer.h"

int strcmp2(const char *a, size_t na, const char *b, size_t nb){
	size_t min = std::min(na, nb);
	for (size_t i = 0; i != min; i++){
		int diff = a[i] - b[i];
		if (diff)
			return diff;
	}
	if (na < nb)
		return -1;
	if (na > nb)
		return 1;
	return 0;
}

bool text_file_line::operator<(const text_file_line &b) const{
	if (this->hash < b.hash)
		return true;
	if (this->hash > b.hash)
		return false;
	int cmp = strcmp2(this->string, this->size, b.string, b.size);
	if (cmp < 0)
		return true;
	if (cmp > 0)
		return false;
	return this->offset < b.offset;
}

template <typename Iterator>
mword_t simple_hash(Iterator begin, const Iterator &end){
	mword_t ret = 0xF00BA8;
	const mword_t factor = sizeof(mword_t) == 8 ? 0x8BADF00DDEADBEEF : 0xDEADBEEF;
	for (; begin != end; ++begin){
		ret *= factor;
		ret ^= *begin;
	}
	return ret;
}

text_file_line *to_text_file_line(const std::string &string, file_offset_t offset){
	auto n = string.size();
	text_file_line *ret = (text_file_line *)malloc(sizeof(text_file_line) + n);
	ret->offset = offset;
	ret->size = n;
	memcpy(ret->string, string.c_str(), n);
	ret->hash = simple_hash(ret->string, ret->string + ret->size);
	return ret;
}

template <typename T>
bool ptrcmp(const T *a, const T *b){
	return *a < *b;
}

std::vector<text_file_line *> split_file(const wchar_t *path){
	BlockByBlockReader reader(path);
	std::vector<text_file_line *> ret;
	std::string accumulator;
	circular_buffer temp(1);
	bool CR = false;
	file_offset_t start_offset = 0;
	while (!reader.at_eof()){
		if (!reader.next_block(temp))
			break;
		temp.process_whole(
			[&accumulator, &ret, &CR, &start_offset](const byte_t *buffer, size_t size){
				for (size_t i = 0; i < size; i++){
					byte_t c = buffer[i];
					bool push = false;
					if (CR){
						CR = false;
						if (c != '\n'){
							ret.push_back(to_text_file_line(accumulator, start_offset));
							start_offset += accumulator.size();
							accumulator.clear();
						}
					}
					accumulator += c;
					switch (c){
						case '\r':
							CR = true;
							break;
						case '\n':
							ret.push_back(to_text_file_line(accumulator, start_offset));
							start_offset += accumulator.size();
							accumulator.clear();
							break;
					}
				}
			}
		);
	}
	ret.push_back(to_text_file_line(accumulator, start_offset));
	std::sort(ret.begin(), ret.end(), ptrcmp<text_file_line>);
	return ret;
}
