#include <iostream>
#include <fstream>
#include <vector>
#define LZMA_API_STATIC
#include <lzma.h>
#include <memory>
#include <exception>

const size_t buffer_size = 1 << 20;

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
	virtual void flush() = 0;
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
		output_buffer.resize(buffer_size);
		this->lstream.next_out = &output_buffer[0];
		this->lstream.avail_out = output_buffer.size();
		this->bytes_read = 0;
		this->bytes_written = 0;
	}
	~LzmaOutputStream(){
		this->flush();
		lzma_end(&this->lstream);
	}
	void write(const void *buffer, size_t size) override{
		do{
			if (this->lstream.avail_in == 0){
				if (!size)
					break;
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

int main(int argc, char **argv){
	if (argc < 3)
		return -1;
	std::ifstream input(argv[1], std::ios::binary);
	if (!input){
		std::cerr << "Error opening file(s).\n";
		return -1;
	}
	std::shared_ptr<OutStream> stream(new FileOutputStream(argv[2]));
	bool mt = false;
	std::shared_ptr<OutStream> stream2(new LzmaOutputStream(stream, 1, false, mt));

	std::vector<char> buffer(buffer_size);

	while (input){
		input.read(&buffer[0], buffer.size());
		stream2->write(&buffer[0], input.gcount());
	}

	return 0;
}
