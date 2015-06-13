#pragma once

template <typename T>
void zero_struct(T &dst){
	memset(&dst, 0, sizeof(dst));
}

template <typename T, size_t N>
void zero_array(T (&dst)[N]){
	memset(&dst, 0, sizeof(dst));
}

inline bool valid_handle(HANDLE handle){
	return handle && handle != INVALID_HANDLE_VALUE;
}

std::wstring path_from_string(const wchar_t *path);
file_size_t get_file_size(const wchar_t *_path);
std::string format_size(double size);

class PrintableBuffer{
	const void *buffer;
	size_t size;
public:
	PrintableBuffer(const void *buffer, size_t size): buffer(buffer), size(size){}
	friend std::ostream &operator<<(std::ostream &, const PrintableBuffer &);
};
