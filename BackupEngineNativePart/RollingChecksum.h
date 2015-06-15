#pragma once

class circular_buffer;

template <typename T>
rolling_checksum_t compute_rsync_rolling_checksum(const T &buffer, size_t size){
	rolling_checksum_t a = 0,
		b = 0;
	for (size_t i = 0; i < size; i++){
		rolling_checksum_t k = buffer[i];
		a += k;
		b = b + k * (rolling_checksum_t)(size - i + 1);
	}
	a &= 0xFFFF;
	b &= 0xFFFF;
	return a | (b << 16);
}

template <typename T>
rolling_checksum_t compute_rsync_rolling_checksum(const T &buffer, size_t piece_size, file_offset_t offset, file_offset_t logical_size, rolling_checksum_t previous = 0){
	rolling_checksum_t a = previous & 0xFFFF,
		b = (previous >> 16) & 0xFFFF;
	for (size_t i = 0; i < piece_size; i++){
		rolling_checksum_t k = buffer[i];
		a += k;
		b = b + k * (rolling_checksum_t)(logical_size - (i + offset) + 1);
	}
	a &= 0xFFFF;
	b &= 0xFFFF;
	return a | (b << 16);
}

rolling_checksum_t compute_rsync_rolling_checksum(const circular_buffer &buffer);
rolling_checksum_t compute_rsync_rolling_checksum(const circular_buffer &buffer, size_t offset, size_t logical_size, rolling_checksum_t previous = 0);
rolling_checksum_t subtract_rsync_rolling_checksum(rolling_checksum_t previous, byte_t byte_to_subtract, size_t size);
rolling_checksum_t add_rsync_rolling_checksum(rolling_checksum_t previous, byte_t byte_to_add, size_t size);
