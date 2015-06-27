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
inline std::string format_size(u64 size){
	return format_size((double)size);
}

class PrintableBuffer{
	const void *buffer;
	size_t size;
public:
	PrintableBuffer(const void *buffer, size_t size): buffer(buffer), size(size){}
	friend std::ostream &operator<<(std::ostream &, const PrintableBuffer &);
};

// Given a range [first, last) and a predicate f such that for al
// first <= x < y !f(x) and for all y <= z < last f(z), find_all() returns y,
// or last if it does not exist.
template<class It, class F>
It find_all(It begin, It end, F &f){
	if (begin >= end)
		return end;
	if (f(*begin))
		return begin;
	auto diff = end - begin;
	while (diff > 1){
		auto pivot = begin + diff / 2;
		if (!f(*pivot))
			begin = pivot;
		else
			end = pivot;
		diff = end - begin;
	}
	return end;
}
