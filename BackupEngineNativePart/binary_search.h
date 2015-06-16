#pragma once

template <typename IndexT, typename FunctionT>
IndexT unbounded_binary_search(IndexT initial, FunctionT &boundary_function){
	if (boundary_function(initial))
		return initial;
	IndexT diff = 1;
	auto border_test = initial + diff;
	while (!boundary_function(border_test) && border_test > initial){
		diff <<= 1;
		border_test = initial + diff;
	}
	if (border_test < initial)
		return initial;
	auto low = initial,
		high = border_test;
	while (low + 1 < high){
		auto pivot = low + (high - low) / 2;
		if (boundary_function(pivot))
			high = pivot;
		else
			low = pivot;
	}
	if (high == low + 1)
		return high;
	return initial;
}

/*
Preconditions:
* begin <= end.
* f partitions the range [begin; end) into three continuous sets x, y, and z, such
  that for all i in x, for all j in y, for all k in z, (i < j < k and f(i) < f(j) < f(k)
  and f(j) == 0).

Postconditions:
* dst_begin <= dst_end.
* [dst_begin; dst_end) is the same set as the set y mentioned in the preconditions.
* If z == [begin; end), dst_end == dst_begin == begin.
* If x == [begin; end), dst_end == dst_begin == end.

Corollary: If no element x in [begin; end) exists such that f(x) == 0 then dst_begin == dst_end.
*/
template <typename Iterator, typename FunctorT>
void triple_search(Iterator &dst_begin, Iterator &dst_end, Iterator begin, Iterator end, FunctorT &f){
	Iterator in;
	dst_begin = nullptr;
	dst_end = nullptr;
	int f_begin, f_end;
	if (begin == end || (f_begin = f(*begin)) > 0){
		dst_end = dst_begin = begin;
		return;
	}
	f_end = f(*(end - 1));
	if (f_end < 0){
		dst_end = dst_begin = end;
		return;
	}
	int count = 0;
	if (!f_begin){
		dst_begin = begin;
		count++;
	}
	if (!f_end){
		dst_end = end;
		count++;
	}
	if (count == 2)
		return;
	count = 0;
	while (begin < end - 1){
		auto pivot = begin + (end - begin) / 2;
		auto i = f(*pivot);
		if (i < 0)
			begin = pivot;
		else if (i > 0)
			end = pivot;
		else{
			in = pivot;
			count = 1;
			break;
		}
	}
	if (!count){
		if (!f(*begin)){
			dst_begin = begin;
			dst_end = end;
		}
		return;
	}

	auto low = begin,
		high = end;

	if (!dst_begin){
		end = in;
		while (begin < end - 1){
			auto pivot = begin + (end - begin) / 2;
			auto i = f(*pivot);
			if (i < 0)
				begin = pivot;
			else
				end = pivot;
		}
		dst_begin = end;
	}

	if (!dst_end){
		begin = in;
		end = high;
		while (begin < end - 1){
			auto pivot = begin + (end - begin) / 2;
			auto i = f(*pivot);
			if (i <= 0)
				begin = pivot;
			else
				end = pivot;
		}
		dst_end = end;
	}
}
