#include "stdafx.h"
#include "RollingChecksum.h"
#include "circular_buffer.h"

rolling_checksum_t compute_rsync_rolling_checksum(const circular_buffer &buffer){
	if (buffer.single_piece())
		return compute_rsync_rolling_checksum(buffer.data(), buffer.size());
	return compute_rsync_rolling_checksum(buffer, buffer.size());
}

rolling_checksum_t compute_rsync_rolling_checksum(const circular_buffer &buffer, size_t offset, size_t logical_size, rolling_checksum_t previous){
	if (buffer.single_piece())
		return compute_rsync_rolling_checksum(buffer.data(), buffer.size(), offset, logical_size, previous);
	return compute_rsync_rolling_checksum(buffer, buffer.size(), offset, logical_size, previous);
}

rolling_checksum_t subtract_rsync_rolling_checksum(rolling_checksum_t previous, byte_t byte_to_subtract, size_t size){
	rolling_checksum_t a = previous & 0xFFFF,
		b = (previous >> 16) & 0xFFFF;
	a += 0x10000;
	a -= byte_to_subtract;
	a &= 0xFFFF;
	b += 0xFFFF0000;
	b -= (rolling_checksum_t)(byte_to_subtract * (size + 1));
	b &= 0xFFFF;
	return a | (b << 16);
}

rolling_checksum_t add_rsync_rolling_checksum(rolling_checksum_t previous, byte_t byte_to_add, size_t size){
	rolling_checksum_t a = previous & 0xFFFF,
		b = (previous >> 16) & 0xFFFF;
	a += byte_to_add;
	a &= 0xFFFF;
	b += a;
	b += byte_to_add;
	b &= 0xFFFF;
	return a | (b << 16);
}

