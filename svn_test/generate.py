import os
import sys
import random

max_line_length = 120
min_line_length = max_line_length / 10

native_slash = '\\'
#native_slash = '/'
def to_native_slash(s):
	return s.replace('/', native_slash)
	#return s

def to_memory_slash(s):
	return s.replace(native_slash, '/')
	#return s


def load_file(path):
	return [s.replace('\n', '') for s in open(path, 'r').readlines()]

def load_list():
	return load_file('wordsEn.txt')

wordlist = load_list()

def pick_word():
	return random.choice(wordlist)

def generate_random_line(override_length = -1):
	ret = ''
	length = random.randint(0, max_line_length)
	if override_length >= 0:
		length = override_length
	if length >= min_line_length or override_length >= 0:
		while len(ret) < length:
			if len(ret) > 0:
				ret += ' '
			ret += pick_word()
	return ret

def generate_new_file(path):
	file = open(path, 'w')
	ret = ''
	lines = random.randint(20, 512)
	for i in range(lines):
		file.write(generate_random_line() + '\n')

def edit_line(line):
	words = line.split(' ')
	if '' in words:
		words.remove('')
	if len(words) > 1:
		start = random.randint(0, len(words) - 1)
		length = min(len(words) - start, random.randint(1, int(len(words) / 2)))
	else:
		start = 0
		length = len(words)
	for i in range(start, start + length):
		words[i] = pick_word()
	return ' '.join(words)

def edit_existing_file(path):
	lines = load_file(path)
	for i in range(random.randint(0, 5)):
		blockstart = random.randint(0, len(lines) - 1)
		r = random.randint(1, 100)
		count = min(len(lines) - blockstart, r)
		for j in range(blockstart, blockstart + count):
			#if j >= len(lines):
			#	print('%d, %d, %d, %d'%(len(lines), blockstart, r, count))
			lines[j] = edit_line(lines[j])
	
	new_lines_count = random.randint(20, 512)
	for i in range(new_lines_count):
		lines.append(generate_random_line())
	
	file = open(path, 'w')
	for line in lines:
		file.write(line + '\n')

def exponential_distribution(k = 2):
	return (k ** random.uniform(1, 10)) / (k ** 10)

def generate_new_binaries_batch(base, addenda):
	for i in range(random.randint(1, 20)):
		path = 'binary.files/%08d.bin'%i
		addenda.append(path)
		file = open(base + '/' + path, 'wb')
		buffer = bytearray(os.urandom(int(exponential_distribution() * 1e+8)))
		file.write(buffer)

def generate_next_version(base, directories, files):
	addenda = []
	if len(files) == 0:
		for i in range(random.randint(10, 100)):
			path = pick_word() + '.txt'
			files.add(path)
			addenda.append(path)
			generate_new_file(base + '/' + path)
		os.mkdir('%s/binary.files'%base)
		addenda.append('binary.files')
		generate_new_binaries_batch(base, addenda)
	else:
		if random.randint(1, 10) == 1:
			#if random.randint(1, 10):
			#	for i in range(random.randint(1, 20)):
			#		path = random.choice(directories
			#else:
			for i in range(random.randint(1, 50)):
				if len(files) == 0:
					break
				path = random.sample(files, 1)[0]
				os.system('svn delete --force ' + to_native_slash(base + '/' + path))
				files.remove(path)
		
		for i in range(min(random.randint(1, 100), len(files))):
			path = random.sample(files, 1)[0]
			edit_existing_file(base + '/' + path)
		
		if random.randint(1, 25) == 1:
			for i in range(random.randint(1, 5)):
				container = None
				if random.randint(0, len(directories)) == 0:
					container = ''
				else:
					container = random.sample(directories, 1)[0] + '/'
				path = container + pick_word()
				directories.add(path)
				addenda.append(path)
				os.mkdir(base + '/' + path)
		
		if random.randint(1, 5) == 1:
			for i in range(random.randint(1, 5)):
				container = None
				if random.randint(0, len(directories)) == 0:
					container = ''
				else:
					container = random.sample(directories, 1)[0] + '/'
				path = container + pick_word() + '.txt'
				files.add(path)
				addenda.append(path)
				generate_new_file(base + '/' + path)
		
		if random.randint(1, 10) == 1:
			generate_new_binaries_batch(base, addenda)
	
	os.chdir(base)
	for i in addenda:
		os.system('svn add %s --force'%(to_native_slash(i)))
	os.chdir('..')
	
	return

def delete_directory(dir):
	os.system('rd /q /s "%s"'%(to_native_slash(dir)))

def isalpha(x):
	return x >= 'a' and x <= 'z' or x >= 'A' and x <= 'Z'

def simple_wd():
	path = os.getcwd()
	print(path)
	if len(path) > 2 and isalpha(path[0]) and path[1] == ':':
		path = path[2:]
	print(path)
	return to_memory_slash(path)

def initialize(central, base):
	delete_directory(central)
	delete_directory(base)
	os.system('svnadmin create %s'%central)
	os.system('svn co file://%s/%s %s'%(simple_wd(), central, base))

def save_version(base):
	print('save_version()')
	os.chdir(base)
	#os.system('svn add *.txt --force')
	cmd = 'svn commit --message "%s"'%generate_random_line(1024)
	os.system(cmd)
	os.chdir('..')
	return

def perform(central, base):
	initialize(central, base)
	directories = set()
	files = set()
	for i in range(1000000):
		generate_next_version(base, directories, files)
		save_version(base)
		if i > 0 and i % 100 == 0:
			os.system('svn cleanup ' + base)

def main():
	perform('central_repo', 'test_repo')
	return

main()
