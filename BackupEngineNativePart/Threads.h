#pragma once

class AutoResetEvent{
	HANDLE event;
	AutoResetEvent(const AutoResetEvent &){}
public:
	AutoResetEvent();
	~AutoResetEvent();
	void set();
	void wait();
};

class Mutex{
	CRITICAL_SECTION mutex;
	Mutex(const Mutex &){}
public:
	Mutex();
	~Mutex();
	void lock();
	void unlock();
};

class AutoMutex{
	Mutex *mutex;
	AutoMutex(const AutoMutex &){}
public:
	AutoMutex(Mutex &m): mutex(&m){
		this->mutex->lock();
	}
	~AutoMutex(){
		this->mutex->unlock();
	}
};
