#pragma once

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

class FileInputStream : public InStream{
	HANDLE file;
public:
	FileInputStream(const wchar_t *path);
	~FileInputStream();
	size_t read(void *buffer, size_t size) override;
	bool eof() override;
};

class FileOutputStream : public OutStream{
	HANDLE file;
public:
	FileOutputStream(const wchar_t *path);
	~FileOutputStream();
	void write(const void *buffer, size_t size) override;
	void flush() override;
};

class DotNetInputStream : public InStream{
public:
	typedef int (*read_callback_t)(std::uint8_t *, int);
	typedef bool (*eof_callback_t)();
	typedef void (*release_callback_t)();
private:
	read_callback_t read_callback;
	eof_callback_t eof_callback;
	release_callback_t release_callback;
public:
	DotNetInputStream(read_callback_t, eof_callback_t, release_callback_t);
	~DotNetInputStream();
	size_t read(void *buffer, size_t size) override;
	bool eof() override;
};

class DotNetOutputStream : public OutStream{
public:
	typedef void (*write_callback_t)(const std::uint8_t *, int);
	typedef void (*flush_callback_t)();
	typedef void (*release_callback_t)();
private:
	write_callback_t write_callback;
	flush_callback_t flush_callback;
	release_callback_t release_callback;
public:
	DotNetOutputStream(write_callback_t, flush_callback_t, release_callback_t);
	~DotNetOutputStream();
	void write(const void *buffer, size_t size) override;
	void flush() override;
};
