#include "stdafx.h"
#include "MiscFunctions.h"

std::wstring path_from_string(const wchar_t *path){
	std::wstring ret = path;
	if (ret.size() >= MAX_PATH - 5 && !(ret[0] == '\\' && ret[1] == '\\' && ret[2] == '?' && ret[3] == '\\'))
		ret = L"\\\\?\\" + ret;
	return ret;
}

file_size_t get_file_size(const wchar_t *_path){
	auto path = path_from_string(_path);
	WIN32_FILE_ATTRIBUTE_DATA fad;
	zero_struct(fad);
	if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &fad))
		return 0;
	file_size_t ret = fad.nFileSizeHigh;
	ret <<= 32;
	ret |= fad.nFileSizeLow;
	return ret;
}

char to_hex(unsigned x){
	return (x < 10 ? '0' : 'a' - 10) + x;
}

void print_hex(std::ostream &stream, const void *buffer, size_t size){
	for (size_t i = 0; i != size; i++){
		byte_t b = ((const byte_t *)buffer)[i];
		stream << to_hex(b >> 4) << to_hex(b & 0x0F);
	}
}

std::ostream &operator<<(std::ostream &stream, const PrintableBuffer &buffer){
	print_hex(stream, buffer.buffer, buffer.size);
	return stream;
}

std::string format_size(double size){
	static const char *units[] = {
		" B",
		" KiB",
		" MiB",
		" GiB",
		" TiB",
		" PiB",
		" EiB",
		" ZiB",
		" YiB"
	};
	int unit = 0;
	bool exact = true;
	while (size >= 1024.0){
		exact &= fmod(size, 1024.0) == 0;
		size /= 1024.0;
		unit++;
	}
	std::stringstream stream;
	if (!exact)
		stream << std::fixed << std::setprecision(1);
	stream << size << units[unit];
	return stream.str();
}

