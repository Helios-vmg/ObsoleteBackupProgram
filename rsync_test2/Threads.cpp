#include "stdafx.h"
#include "Threads.h"
#include "MiscFunctions.h"

AutoResetEvent::AutoResetEvent(){
	this->event = CreateEvent(nullptr, false, false, nullptr);
}

AutoResetEvent::~AutoResetEvent(){
	CloseHandle(this->event);
}

void AutoResetEvent::set(){
	SetEvent(this->event);
}

void AutoResetEvent::wait(){
	WaitForSingleObject(this->event, INFINITE);
}

Mutex::Mutex(){
	zero_struct(this->mutex);
	InitializeCriticalSection(&this->mutex);
}

Mutex::~Mutex(){
	DeleteCriticalSection(&this->mutex);
}

void Mutex::lock(){
	EnterCriticalSection(&this->mutex);
}

void Mutex::unlock(){
	LeaveCriticalSection(&this->mutex);
}
