#include "stdafx.h"
#include "circular_buffer.h"

circular_buffer::circular_buffer(size_t initial_size): buffer(nullptr), m_capacity(0){
	this->realloc(initial_size);
}

circular_buffer::~circular_buffer(){
	delete[] this->buffer;
}

void circular_buffer::realloc(size_t n){
	if (this->m_capacity != n){
		delete[] this->buffer;
		this->buffer = new byte_t[n];
	}
	this->m_capacity = n;
	this->reset();
}

byte_t circular_buffer::pop(){
	if (!this->m_size)
		return 0;
	auto ret = this->buffer[this->start];
	this->start++;
	this->start %= this->m_capacity;
	this->m_size--;
	return ret;
}

byte_t circular_buffer::push(byte_t ret){
	if (this->m_size == this->m_capacity)
		return 0;
	this->buffer[(this->start + this->m_size++) % this->m_capacity] = ret;
	return ret;
}

void circular_buffer::reset(){
	this->m_size = this->m_capacity;
	this->start = 0;
}
