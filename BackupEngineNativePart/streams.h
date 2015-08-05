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

class FileOutputStream : public OutStream{
	HANDLE file;
public:
	FileOutputStream(const wchar_t *path);
	~FileOutputStream();
	void write(const void *buffer, size_t size) override;
	void flush() override;
};

class FileInputStream : public InStream{
	HANDLE file;
public:
	FileInputStream(const wchar_t *path);
	~FileInputStream();
	size_t read(void *buffer, size_t size) override;
	bool eof() override;
};
