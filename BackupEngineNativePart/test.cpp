#include "stdafx.h"
#include "Rsync.h"
#include "FileComparer.h"
#include "RsyncableFile.h"
#include "ExportedFunctions.h"
#include "MiscFunctions.h"

EXPORT_THIS void test_func(){
	static const wchar_t *paths[] = {
		L"version0.bin",
		L"version1.bin",
		L"version2.bin",
		L"version3.bin",
		L"version4.bin",
	};
	FileComparer cmp0(paths[1], std::shared_ptr<RsyncableFile>(new RsyncableFile(paths[0])));
	cmp0.process();
	FileComparer cmp1(paths[2], std::shared_ptr<RsyncableFile>(new RsyncableFile(cmp0)));
	cmp1.process();
	FileComparer cmp2(paths[3], std::shared_ptr<RsyncableFile>(new RsyncableFile(cmp1)));
	cmp2.process();
	FileComparer cmp3(paths[4], std::shared_ptr<RsyncableFile>(new RsyncableFile(cmp2)));
	cmp3.process();
	std::shared_ptr<rsync::NormalFile> file0(new rsync::NormalFile(paths[0], 0));
	std::shared_ptr<rsync::NormalFile> file1(new rsync::NormalFile(paths[1], 1));
	std::shared_ptr<rsync::NormalFile> file2(new rsync::NormalFile(paths[2], 2));
	std::shared_ptr<rsync::NormalFile> file3(new rsync::NormalFile(paths[3], 3));
	std::shared_ptr<rsync::NormalFile> file4(new rsync::NormalFile(paths[3], 4));
	auto results = cmp0.get_result();
	std::shared_ptr<rsync::RsyncChainLink> link0(new rsync::RsyncChainLink(file0, file1, &(*results)[0], results->size()));
	results = cmp1.get_result();
	std::shared_ptr<rsync::RsyncChainLink> link1(new rsync::RsyncChainLink(link0, file2, &(*results)[0], results->size()));
	results = cmp2.get_result();
	std::shared_ptr<rsync::RsyncChainLink> link2(new rsync::RsyncChainLink(link1, file3, &(*results)[0], results->size()));


	{
		file_size_t size = 0;
		for (auto &c : *results)
			size += c.get_length();
		std::vector<rsync::reconstructed_part> parts;
		link2->reconstruct_section(parts, 0, size);

		for (auto &p : parts)
			std::cout << "Copy from file ID " << p.source << " from offset " << p.offset_in_source << " " << p.size << " bytes (" << format_size(p.size) << ").\n";
	}

	results = cmp3.get_result();
	std::shared_ptr<rsync::RsyncChainLink> link3(new rsync::RsyncChainLink(link2, file4, &(*results)[0], results->size()));

	{
		std::cout << "\n\n";

		file_size_t size = 0;
		for (auto &c : *results)
			size += c.get_length();

		std::vector<rsync::reconstructed_part> parts;
		link3->reconstruct_section(parts, 0, size);

		for (auto &p : parts)
			std::cout << "Copy from file ID " << p.source << " from offset " << p.offset_in_source << " " << p.size << " bytes (" << format_size(p.size) << ").\n";
	}

	return;
	{
		std::ofstream output(L"test_version.bin", std::ios::binary);
		byte_t buffer[1 << 13];
		file_offset_t offset = 0;
		while (!link2->eof()){
			size_t size = 1 << 13;
			size_t read;
			if (!link2->read(buffer, size, read))
				break;
			offset += read;
			output.write((const char *)buffer, read);
		}
	}
}
