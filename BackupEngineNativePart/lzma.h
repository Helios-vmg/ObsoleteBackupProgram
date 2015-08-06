#pragma once
#include "streams.h"

const size_t default_buffer_size = 1 << 20;

class LzmaInitializationException : public std::exception{
	std::string message;
public:
	LzmaInitializationException(const char *msg) : message(msg){}
	const char *what() const override{
		return this->message.c_str();
	}
};

class LzmaOperationException : public std::exception{
	std::string message;
public:
	LzmaOperationException(const char *msg) : message(msg){}
	const char *what() const override{
		return this->message.c_str();
	}
};

class LzmaOutputStream : public OutStream{
	std::shared_ptr<OutStream> stream;
	lzma_stream lstream;
	lzma_action action;
	std::vector<uint8_t> output_buffer;
	uint64_t bytes_read,
		bytes_written;

	bool pass_data_to_stream(lzma_ret ret);
public:
	LzmaOutputStream(std::shared_ptr<OutStream> wrapped_stream, bool &multithreaded, int compression_level = 5, size_t buffer_size = default_buffer_size, bool extreme_mode = false);
	~LzmaOutputStream();
	void write(const void *buffer, size_t size) override;
	void flush() override;
};

class LzmaInputStream : public InStream{
	std::shared_ptr<InStream> stream;
	lzma_stream lstream;
	lzma_action action;
	std::vector<uint8_t> input_buffer;
	const uint8_t *queued_buffer;
	size_t queued_bytes;
	uint64_t bytes_read,
		bytes_written;
	bool at_eof;
public:
	LzmaInputStream(std::shared_ptr<InStream> wrapped_stream, size_t buffer_size = default_buffer_size);
	~LzmaInputStream();
	size_t read(void *buffer, size_t size) override;
	bool eof() override;
};
