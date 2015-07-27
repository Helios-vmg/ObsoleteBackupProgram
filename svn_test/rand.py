import math
import random

def f():
	k = 2
	return (k ** random.uniform(1, 10)) / (k ** 10) * 10

map = {}
n = 100000
for i in range(n):
	x = int(f())
	if x in map:
		map[x] += 1
	else:
		map[x] = 1

for i in map:
	print(i, map[i] / n * 100)
