struct text_file_line{
	mword_t hash;
	file_offset_t offset;
	size_t size;
	char string[1];

	bool operator<(const text_file_line &) const;
};

text_file_line *to_text_file_line(const std::string &, file_offset_t);

std::vector<text_file_line *> split_file(const wchar_t *path);
