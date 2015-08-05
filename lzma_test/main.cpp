#include <iostream>
#include <fstream>
#include <vector>
#define LZMA_API_STATIC
#include <lzma.h>

const size_t buffer_size = 1 << 20;

int main(int argc, char **argv){
	if (argc < 3)
		return -1;
	std::ifstream input(argv[1], std::ios::binary);
	std::ofstream output(argv[2], std::ios::binary);
	if (!input || !output){
		std::cerr << "Error opening file(s).\n";
		return -1;
	}

	lzma_stream strm = LZMA_STREAM_INIT;
	uint32_t preset = 1 /*| LZMA_PRESET_EXTREME*/;
	lzma_ret ret = lzma_easy_encoder(&strm, preset, LZMA_CHECK_CRC64);

	if (ret != LZMA_OK){
		const char *msg;
		switch (ret) {
			case LZMA_MEM_ERROR:
				msg = "Memory allocation failed.\n";
				break;
			case LZMA_OPTIONS_ERROR:
				msg = "Specified preset is not supported.\n";
				break;
			case LZMA_UNSUPPORTED_CHECK:
				msg = "Specified integrity check is not supported.\n";
				break;
			default:
				msg = "Unknown error.\n";
				break;
		}
		std::cerr << "Error initializing liblzma: " << msg;
		return -2;
	}

	lzma_action action = LZMA_RUN;

	std::vector<char> in_buffer;
	std::vector<char> out_buffer;
	out_buffer.resize(buffer_size);
	strm.next_out = (uint8_t *)&out_buffer[0];
	strm.avail_out = out_buffer.size();

	uint64_t bytes_read = 0,
		bytes_written = 0;
	while (true){
		if (strm.avail_in == 0 && !input.eof()) {
			in_buffer.resize(buffer_size);
			input.read(&in_buffer[0], in_buffer.size());
			in_buffer.resize(input.gcount());
			bytes_read += in_buffer.size();
			strm.next_in = (const uint8_t *)&in_buffer[0];
			strm.avail_in = in_buffer.size();

			if (input.eof())
				action = LZMA_FINISH;
			else if (!input){
				std::cerr << "Error reading input file.\n";
				action = LZMA_FINISH;
			}
			std::cout << "\rRead: " << bytes_read / (1024.0 * 1024.0) << " MiB - Written " << bytes_written / (1024.0 * 1024.0) << " MiB - Ratio: " << bytes_written * 100 / bytes_read << " %           ";
		}

		auto ret = lzma_code(&strm, action);

		if (!strm.avail_out || ret == LZMA_STREAM_END) {
			size_t write_size = out_buffer.size() - strm.avail_out;

			output.write(&out_buffer[0], write_size);
			if (!output){
				std::cerr << "Error writing output file.\n";
				break;
			}

			bytes_written += write_size;

			std::cout << "\rRead: " << bytes_read / (1024.0 * 1024.0) << " MiB - Written " << bytes_written / (1024.0 * 1024.0) << " MiB - Ratio: " << bytes_written * 100 / bytes_read << " %           ";

			strm.next_out = (uint8_t *)&out_buffer[0];
			strm.avail_out = out_buffer.size();
		}

		if (ret != LZMA_OK) {
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
				std::cerr << msg;
			}
			break;
		}
	}
	lzma_end(&strm);
	return 0;
}
