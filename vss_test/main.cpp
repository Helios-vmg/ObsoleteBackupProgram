#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <iostream>
#include <fstream>
#include <exception>
#include <memory>
#include <cstdint>
#include <iomanip>
#include <string>

#pragma comment(lib, "vssapi.lib")

struct HresultException{
	HRESULT hres;
	const char *context;
	HresultException(const char *context, HRESULT hres) : hres(hres), context(context){}
};

class WriterMetadata;
class WriterStatus;

class VssAsync{
	IVssAsync *async;
public:
	VssAsync(IVssAsync *async) : async(async){}
	~VssAsync(){
		if (this->async)
			this->async->Release();
	}
	void wait(){
		auto hres = this->async->Wait();
		if (FAILED(hres))
			throw HresultException("IVssAsync::Wait", hres);
	}
};

class SnapshotProperties{
	VSS_SNAPSHOT_PROP props;
public:
	SnapshotProperties(const VSS_SNAPSHOT_PROP &props) : props(props){
	}
	~SnapshotProperties(){
		VssFreeSnapshotProperties(&this->props);
	}
	VSS_SNAPSHOT_PROP get_properties() const{
		return this->props;
	}
};

class BackupComponents{
	IVssBackupComponents *vbc;
public:
	BackupComponents() : vbc(nullptr){
		auto hres = CreateVssBackupComponents(&vbc);
		if (FAILED(hres)){
			this->vbc = nullptr;
			throw HresultException("CreateVssBackupComponents", hres);
		}
	}

	~BackupComponents(){
		if (this->vbc)
			this->vbc->Release();
	}

	void initialize_for_backup(){
		auto hres = this->vbc->InitializeForBackup();
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::InitializeForBackup", hres);
	}

	std::shared_ptr<WriterMetadata> gather_writer_metadata();

	void set_context(LONG context){
		auto hres = this->vbc->SetContext(context);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::SetContext", hres);
	}

	void free_writer_metadata(){
		auto hres = this->vbc->FreeWriterMetadata();
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::FreeWriterMetadata", hres);
	}

	VSS_ID start_snapshot_set(){
		VSS_ID ret;
		auto hres = this->vbc->StartSnapshotSet(&ret);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::StartSnapshotSet", hres);
		return ret;
	}

	VSS_ID add_to_snapshot_set(){
		VSS_ID ret;
		auto hres = this->vbc->AddToSnapshotSet(L"F:\\", GUID_NULL, &ret);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::AddToSnapshotSet", hres);
		return ret;
	}

	void set_backup_state(){
		auto hres = this->vbc->SetBackupState(false, true, VSS_BT_COPY);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::SetBackupState", hres);
	}

	void prepare_for_backup(){
		IVssAsync *async = nullptr;
		auto hres = this->vbc->PrepareForBackup(&async);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::PrepareForBackup", hres);
		{
			VssAsync async2(async);
			async2.wait();
		}
	}

	std::shared_ptr<WriterStatus> gather_writer_status();

	void free_writer_status(){
		auto hres = this->vbc->FreeWriterStatus();
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::FreeWriterStatus", hres);
	}

	void do_snapshot_set(){
		IVssAsync *async = nullptr;
		auto hres = this->vbc->DoSnapshotSet(&async);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::DoSnapshotSet", hres);
		{
			VssAsync async2(async);
			async2.wait();
		}
	}

	void backup_complete(){
		IVssAsync *async = nullptr;
		auto hres = this->vbc->BackupComplete(&async);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::BackupComplete", hres);
		{
			VssAsync async2(async);
			async2.wait();
		}
	}

	void delete_snapshot_set(const VSS_ID &snapshot_set_id){
		LONG deleted_snapshots;
		VSS_ID undeleted;
		auto hres = this->vbc->DeleteSnapshots(snapshot_set_id, VSS_OBJECT_SNAPSHOT_SET, true, &deleted_snapshots, &undeleted);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::DeleteSnapshots", hres);
	}

	std::shared_ptr<SnapshotProperties> get_snapshot_properties(const VSS_ID &snapshot_set_id){
		VSS_SNAPSHOT_PROP props;
		auto hres = this->vbc->GetSnapshotProperties(snapshot_set_id, &props);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::SnapshotProperties", hres);
		return std::shared_ptr<SnapshotProperties>(new SnapshotProperties(props));
	}
};

class WriterMetadata{
	BackupComponents *bc;
public:
	WriterMetadata(BackupComponents &bc) : bc(&bc){}
	~WriterMetadata(){
		this->bc->free_writer_metadata();
	}
};

class WriterStatus{
	BackupComponents *bc;
public:
	WriterStatus(BackupComponents &bc) : bc(&bc){}
	~WriterStatus(){
		this->bc->free_writer_status();
	}
};

std::shared_ptr<WriterMetadata> BackupComponents::gather_writer_metadata(){
	IVssAsync *async = nullptr;
	auto hres = this->vbc->GatherWriterMetadata(&async);
	if (FAILED(hres))
		throw HresultException("IVssBackupComponents::GatherWriterMetadata", hres);
	{
		VssAsync async2(async);
		async2.wait();
	}
	return std::shared_ptr<WriterMetadata>(new WriterMetadata(*this));
}

std::shared_ptr<WriterStatus> BackupComponents::gather_writer_status(){
	IVssAsync *async = nullptr;
	auto hres = this->vbc->GatherWriterStatus(&async);
	if (FAILED(hres))
		throw HresultException("IVssBackupComponents::GatherWriterStatus", hres);
	{
		VssAsync async2(async);
		async2.wait();
	}
	return std::shared_ptr<WriterStatus>(new WriterStatus(*this));
}

typedef std::uint8_t byte_t;

class CRC32{
public:
	typedef std::uint32_t crc32_t;
private:
	crc32_t crc32;
	static crc32_t CRC32lookup[];
public:
	CRC32(){
		this->Reset();
	}
	void Reset(){
		this->crc32 ^= ~this->crc32;
	}
	void Input(const void *message_array, size_t length){
		auto i = (const byte_t *)message_array;
		auto end = i + length;
		for (; i != end; i++)
			this->Input(*i);
	}
	void Input(byte_t message_element){
		this->crc32 = (this->crc32 >> 8) ^ CRC32lookup[message_element ^ (this->crc32 & 0xFF)];
	}
	crc32_t Result(){
		return ~this->crc32;
	}
};

CRC32::crc32_t CRC32::CRC32lookup[] = {
	0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F, 0xE963A535, 0x9E6495A3,
	0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988, 0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91,
	0x1DB71064, 0x6AB020F2, 0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
	0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9, 0xFA0F3D63, 0x8D080DF5,
	0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172, 0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B,
	0x35B5A8FA, 0x42B2986C, 0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
	0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423, 0xCFBA9599, 0xB8BDA50F,
	0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924, 0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D,
	0x76DC4190, 0x01DB7106, 0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
	0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D, 0x91646C97, 0xE6635C01,
	0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E, 0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457,
	0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
	0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7, 0xA4D1C46D, 0xD3D6F4FB,
	0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0, 0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9,
	0x5005713C, 0x270241AA, 0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
	0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81, 0xB7BD5C3B, 0xC0BA6CAD,
	0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A, 0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683,
	0xE3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
	0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB, 0x196C3671, 0x6E6B06E7,
	0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC, 0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5,
	0xD6D6A3E8, 0xA1D1937E, 0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
	0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55, 0x316E8EEF, 0x4669BE79,
	0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236, 0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F,
	0xC5BA3BBE, 0xB2BD0B28, 0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
	0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F, 0x72076785, 0x05005713,
	0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21,
	0x86D3D2D4, 0xF1D4E242, 0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
	0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69, 0x616BFFD3, 0x166CCF45,
	0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2, 0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB,
	0xAED16A4A, 0xD9D65ADC, 0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
	0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693, 0x54DE5729, 0x23D967BF,
	0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94, 0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D
};


std::uint32_t calculate_crc32(const char *path){
	std::ifstream file(path, std::ios::binary);
	if (!file)
		return ~0;
	byte_t buffer[1 << 12];
	CRC32 crc32;
	while (file){
		file.read((char *)buffer, sizeof(buffer));
		auto bytes_read = file.gcount();
		crc32.Input(buffer, bytes_read);
	}
	return crc32.Result();
}

void coinitialize(){
	auto hres = CoInitialize(nullptr);
	if (FAILED(hres))
		throw HresultException("CoInitialize", hres);
}

std::wostream &operator<<(std::wostream &stream, const VSS_ID &guid){
	LPOLESTR temp;
	auto hres = StringFromCLSID(guid, &temp);
	if (FAILED(hres))
		std::cerr << "StringFromCLSID() failed: " << std::hex << std::setw(8) << std::setfill('0') << hres << std::endl;
	else
		stream << temp;
	CoTaskMemFree(temp);
	return stream;
}

int main(int argc, char **argv){
	if (argc < 2)
		return 0;
	auto normal_read_result = calculate_crc32(argv[1]);
	std::cout << "Normal read: " << std::hex << std::setw(8) << std::setfill('0') << normal_read_result << std::endl;
	if (argc < 3)
		return 0;
	try{
		coinitialize();
		BackupComponents comps;
		comps.initialize_for_backup();
		const auto context = VSS_CTX_BACKUP | VSS_CTX_CLIENT_ACCESSIBLE_WRITERS | VSS_CTX_APP_ROLLBACK;
		auto metadata = comps.gather_writer_metadata();
		comps.set_context(context);
		auto snapshot_set_id = comps.start_snapshot_set();
		std::wcout << "Snapshot ID: " << snapshot_set_id << std::endl;
		auto shadow_id = comps.add_to_snapshot_set();
		std::wcout << "Shadow ID: " << shadow_id << std::endl;
		comps.set_backup_state();
		comps.prepare_for_backup();
		auto status = comps.gather_writer_status();
		comps.do_snapshot_set();

		//auto vss_read_result = calculate_crc32(argv[1]);
		//std::cout << "VSS read: " << std::hex << std::setw(8) << std::setfill('0') << vss_read_result << std::endl;
		{
			try{
				auto props = comps.get_snapshot_properties(shadow_id);
				auto props2 = props->get_properties();
				std::wcout
#define PRINT(x) << #x " " << x << std::endl
#define PRINT2(x) << #x " " << (x ? x : L"NULL") << std::endl
					PRINT(props2.m_SnapshotId)
					PRINT(props2.m_SnapshotSetId)
					PRINT(props2.m_lSnapshotsCount)
					PRINT2(props2.m_pwszSnapshotDeviceObject)
					PRINT2(props2.m_pwszOriginalVolumeName)
					PRINT2(props2.m_pwszOriginatingMachine)
					PRINT2(props2.m_pwszServiceMachine)
					PRINT2(props2.m_pwszExposedName)
					PRINT2(props2.m_pwszExposedPath)
					PRINT(props2.m_ProviderId)
					PRINT(props2.m_lSnapshotAttributes)
					PRINT(props2.m_tsCreationTimestamp)
					PRINT(props2.m_eStatus)
					;

				{
					auto wstr = props2.m_pwszSnapshotDeviceObject;
					auto length = wcslen(wstr);
					std::string path;
					path.resize(length);
					std::copy(wstr, wstr + length, path.begin());
					path += argv[1];
					std::cout << path << std::endl;
					auto vss_read_result = calculate_crc32(path.c_str());
					std::cout << "VSS read: " << std::hex << std::setw(8) << std::setfill('0') << vss_read_result << std::endl;
				}
			}catch (HresultException &e){
				std::cerr << e.context << "() failed with error: " << std::hex << std::setw(8) << std::setfill('0') << e.hres << std::endl;
			}
		}

		status = comps.gather_writer_status();
		comps.backup_complete();
		comps.delete_snapshot_set(snapshot_set_id);
	}catch (HresultException &e){
		std::cerr << e.context << "() failed with error: " << std::hex << std::setw(8) << std::setfill('0') << e.hres << std::endl;
	}
}
