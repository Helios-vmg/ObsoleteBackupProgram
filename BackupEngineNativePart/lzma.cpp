#include "stdafx.h"
#define LZMA_API_STATIC
#include <lzma.h>
#include "lzma.h"

const size_t buffer_size = 1 << 12;

bool LzmaOutputStream::pass_data_to_stream(lzma_ret ret){
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
				msg = "Memory allocation failed.";
				break;
			case LZMA_DATA_ERROR:
				msg = "File size limits exceeded.";
				break;
			default:
				msg = "Unknown error.";
				break;
			}
			throw LzmaOperationException(msg);
		}
		return false;
	}
	return true;
}

LzmaOutputStream::LzmaOutputStream(std::shared_ptr<OutStream> wrapped_stream, int compression_level, bool extreme_mode, bool &multithreaded){
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
			msg = "Memory allocation failed.";
			break;
		case LZMA_OPTIONS_ERROR:
			msg = "Specified compression level is not supported.";
			break;
		case LZMA_UNSUPPORTED_CHECK:
			msg = "Specified integrity check is not supported.";
			break;
		default:
			msg = "Unknown error.";
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

LzmaOutputStream::~LzmaOutputStream(){
	this->flush();
	lzma_end(&this->lstream);
}

void LzmaOutputStream::write(const void *buffer, size_t size){
	lzma_ret ret;
	do{
		if (this->lstream.avail_in == 0){
			if (!size)
				break;
			this->bytes_read += size;
			this->lstream.next_in = (const uint8_t *)buffer;
			this->lstream.avail_in = size;
			size = 0;
		}
		ret = lzma_code(&this->lstream, this->action);
	} while (this->pass_data_to_stream(ret));
}

void LzmaOutputStream::flush(){
	if (this->action != LZMA_RUN)
		return;
	this->action = LZMA_FINISH;
	while (this->pass_data_to_stream(lzma_code(&this->lstream, this->action)));
	this->stream->flush();
}

LzmaInputStream::LzmaInputStream(std::shared_ptr<InStream> wrapped_stream) : at_eof(false){
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

LzmaInputStream::~LzmaInputStream(){
	lzma_end(&this->lstream);
}

size_t LzmaInputStream::read(void *buffer, size_t size){
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

bool LzmaInputStream::eof(){
	return this->at_eof;
}
