#include <iostream>
#include <fstream>
#include <vector>
#define LZMA_API_STATIC
#include <lzma.h>
#include <memory>
#include <exception>
#include <algorithm>

const size_t buffer_size = 1 << 12;

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

class InStream{
public:
	virtual ~InStream(){}
	virtual size_t read(void *buffer, size_t size) = 0;
	virtual bool eof() = 0;
};

class OutStream{
public:
	virtual ~OutStream(){}
	virtual void write(const void *buffer, size_t size) = 0;
	virtual void flush() = 0;
};

class LzmaOutputStream : public OutStream{
	std::shared_ptr<OutStream> stream;
	lzma_stream lstream;
	lzma_action action;
	std::vector<uint8_t> output_buffer;
	uint64_t bytes_read,
		bytes_written;

	bool pass_data_to_stream(lzma_ret ret){
		if (!this->lstream.avail_out || ret == LZMA_STREAM_END) {
			size_t write_size = this->output_buffer.size() - this->lstream.avail_out;

			this->stream->write(&this->output_buffer[0], write_size);

			this->bytes_written += write_size;

			this->lstream.next_out = &this->output_buffer[0];
			this->lstream.avail_out = this->output_buffer.size();
		}

		if (ret != LZMA_OK){
			if (ret != LZMA_STREAM_END){
				const char *msg;
				switch (ret) {
					case LZMA_MEM_ERROR:
						msg = "Memory allocation failed.\n";
						break;
					case LZMA_DATA_ERROR:
						msg = "File size limits exceeded.\n";
						break;
					default:
						msg = "Unknown error.\n";
						break;
				}
				throw LzmaOperationException(msg);
			}
			return false;
		}
		return true;
	}
public:
	LzmaOutputStream(std::shared_ptr<OutStream> wrapped_stream, int compression_level, bool extreme_mode, bool &multithreaded){
		this->stream = wrapped_stream;
		multithreaded = false;
		this->lstream = LZMA_STREAM_INIT;
		uint32_t preset = compression_level;
		if (extreme_mode)
			preset |= LZMA_PRESET_EXTREME;
		lzma_ret ret = lzma_easy_encoder(&this->lstream, preset, LZMA_CHECK_NONE);
		if (ret != LZMA_OK){
			const char *msg;
			switch (ret) {
				case LZMA_MEM_ERROR:
					msg = "Memory allocation failed.\n";
					break;
				case LZMA_OPTIONS_ERROR:
					msg = "Specified compression level is not supported.\n";
					break;
				case LZMA_UNSUPPORTED_CHECK:
					msg = "Specified integrity check is not supported.\n";
					break;
				default:
					msg = "Unknown error.\n";
					break;
			}
			throw LzmaInitializationException(msg);
		}
		this->action = LZMA_RUN;
		this->output_buffer.resize(buffer_size);
		this->lstream.next_out = &this->output_buffer[0];
		this->lstream.avail_out = this->output_buffer.size();
		this->bytes_read = 0;
		this->bytes_written = 0;
	}
	~LzmaOutputStream(){
		this->flush();
		lzma_end(&this->lstream);
	}
	void write(const void *buffer, size_t size) override{
		do{
			if (this->lstream.avail_in == 0 && size){
				this->bytes_read += size;
				this->lstream.next_in = (const uint8_t *)buffer;
				this->lstream.avail_in = size;
				size = 0;
			}
		}while (this->pass_data_to_stream(lzma_code(&this->lstream, this->action)));
	}
	void flush() override{
		if (this->action != LZMA_RUN)
			return;
		this->action = LZMA_FINISH;
		this->pass_data_to_stream(lzma_code(&this->lstream, this->action));
		this->stream->flush();
	}
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
	LzmaInputStream(std::shared_ptr<InStream> wrapped_stream): at_eof(false){
		this->stream = wrapped_stream;
		this->lstream = LZMA_STREAM_INIT;
		lzma_ret ret = lzma_stream_decoder(&this->lstream, UINT64_MAX, LZMA_IGNORE_CHECK);
		if (ret != LZMA_OK){
			const char *msg;
			switch (ret) {
				case LZMA_MEM_ERROR:
					msg = "Memory allocation failed.\n";
					break;
				case LZMA_OPTIONS_ERROR:
					msg = "Unsupported decompressor flags.\n";
					break;
				default:
					msg = "Unknown error.\n";
					break;
			}
			throw LzmaInitializationException(msg);
		}
		this->action = LZMA_RUN;
		this->input_buffer.resize(buffer_size);
		this->bytes_read = 0;
		this->bytes_written = 0;
		this->queued_buffer = &this->input_buffer[0];
		this->queued_bytes = 0;
	}
	~LzmaInputStream(){
		lzma_end(&this->lstream);
	}
	size_t read(void *buffer, size_t size) override{
		if (this->eof())
			return 0;
		size_t ret = 0;
		this->lstream.next_out = (uint8_t *)buffer;
		this->lstream.avail_out = size;
		while (this->lstream.avail_out){
			if (this->lstream.avail_in == 0 && !this->stream->eof()){
				this->lstream.next_in = &this->input_buffer[0];
				this->lstream.avail_in = this->stream->read(&this->input_buffer[0], this->input_buffer.size());
				this->bytes_read += this->lstream.avail_in;
				if (this->stream->eof()){
					this->action = LZMA_FINISH;
				}
			}
			lzma_ret ret_code = lzma_code(&this->lstream, action);
			if (ret_code != LZMA_OK) {
				if (ret_code == LZMA_STREAM_END)
					break;
				const char *msg;
				switch (ret_code) {
					case LZMA_MEM_ERROR:
						msg = "Memory allocation failed.";
						break;
					case LZMA_FORMAT_ERROR:
						msg = "The input is not in the .xz format.";
						break;
					case LZMA_OPTIONS_ERROR:
						msg = "Unsupported compression options.";
						break;
					case LZMA_DATA_ERROR:
						msg = "Compressed file is corrupt.";
						break;
					case LZMA_BUF_ERROR:
						msg = "Compressed file is truncated or otherwise corrupt.";
						break;
					default:
						msg = "Unknown error.";
						break;
				}
				throw LzmaOperationException(msg);
			}
		}
		ret = size - this->lstream.avail_out;
		this->bytes_written += ret;
		this->at_eof = !ret;
		return ret;
	}
	bool eof() override{
		return this->at_eof;
	}
};

class FileOutputStream : public OutStream{
	std::ofstream stream;
public:
	FileOutputStream(const char *path) : stream(path, std::ios::binary){
		if (!this->stream)
			throw std::string("Error opening ") + path;
	}
	void write(const void *buffer, size_t size) override{
		this->stream.write((const char *)buffer, size);
	}
	void flush() override{
		this->stream.flush();
	}
};

class FileInputStream : public InStream{
	std::ifstream stream;
public:
	FileInputStream(const char *path) : stream(path, std::ios::binary){
		if (!this->stream)
			throw std::string("Error opening ") + path;
	}
	size_t read(void *buffer, size_t size) override{
		this->stream.read((char *)buffer, size);
		if (this->stream.bad())
			throw std::string("I/O error while reading file.");
		return this->stream.gcount();
	}
	bool eof() override{
		return this->stream.eof();
	}
};

int main(int argc, char **argv){
	if (argc < 3)
		return -1;
#if 1
	std::shared_ptr<InStream> in_stream_0(new FileInputStream(argv[1]));
	std::shared_ptr<InStream> in_stream(new LzmaInputStream(in_stream_0));
	std::shared_ptr<OutStream> out_stream(new FileOutputStream(argv[2]));
#else
	std::shared_ptr<InStream> in_stream(new FileInputStream(argv[1]));
	std::shared_ptr<OutStream> out_stream_0(new FileOutputStream(argv[2]));
	bool mt = false;
	std::shared_ptr<OutStream> out_stream(new LzmaOutputStream(out_stream_0, 1, false, mt));
#endif
	std::vector<char> buffer(buffer_size);

	while (!in_stream->eof()){
		auto read = in_stream->read(&buffer[0], buffer.size());
		out_stream->write(&buffer[0], read);
	}
	return 0;
}
