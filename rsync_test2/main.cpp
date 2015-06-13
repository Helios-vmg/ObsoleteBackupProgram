#include "stdafx.h"
#include "FileComparer.h"
#include "RsyncableFile.h"
#include "MiscTypes.h"
#include "MiscFunctions.h"

int main(){
	auto t0 = clock();
	FileComparer cmp(L"g:/Backup/000/version1.zip", std::shared_ptr<RsyncableFile>(new RsyncableFile(L"g:/Backup/000/version0.zip")));
	auto t1 = clock();
	cmp.process();
	auto t2 = clock();
	std::cout << "Building rsync table: " << double(t1 - t0) / CLOCKS_PER_SEC << " s.\n"
		"Comparing files: " << double(t2 - t1) / CLOCKS_PER_SEC << " s.\n";

	std::cout
		<< "Old SHA1: " << PrintableBuffer(cmp.get_old_digest(), 20) << "\n"
		"New SHA1: " << PrintableBuffer(cmp.get_new_digest(), 20) << std::endl;
	return 0;
}
