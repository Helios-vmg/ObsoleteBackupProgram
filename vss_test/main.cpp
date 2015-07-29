#include <vss.h>
#include <vswriter.h>
#include <vsbackup.h>
#include <iostream>
#include <exception>
#include <memory>

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

	void add_to_snapshot_set(VSS_ID snapshot_set_id){
		auto hres = this->vbc->AddToSnapshotSet(L"C:\\", GUID_NULL, &snapshot_set_id);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::AddToSnapshotSet", hres);
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

int main(){
	try{
		BackupComponents comps;
		comps.initialize_for_backup();
		const auto context = VSS_CTX_BACKUP | VSS_CTX_CLIENT_ACCESSIBLE_WRITERS | VSS_CTX_APP_ROLLBACK;
		auto metadata = comps.gather_writer_metadata();
		comps.set_context(context);
		auto snapshot_set_id = comps.start_snapshot_set();
		comps.add_to_snapshot_set(snapshot_set_id);
		comps.prepare_for_backup();
	}catch (HresultException &e){
		std::cerr << e.context << "() failed with error: " << e.hres << std::endl;
	}
}
