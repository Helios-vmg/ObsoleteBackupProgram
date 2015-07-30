#include "stdafx.h"
#include "vss.h"
#include <comdef.h>

HresultException::HresultException(const char *context, HRESULT hres){
	std::stringstream stream;
	stream << context << " failed with error 0x"
		<< std::hex << std::setw(8) << std::setfill('0') << hres
		<< " (";
	{
		_com_error error(hres);
		for (auto p = error.ErrorMessage(); p; p++)
			stream << ((unsigned)p < 0x80 ? (char)p : '?');
	}
	stream << ")";
}

SnapshotProperties::SnapshotProperties(){
}

std::vector<VSS_ID> SnapshotProperties::get_shadow_ids() const{
	std::vector<VSS_ID> ret;
	ret.reserve(this->shadows.size());
	for (const auto &shadow : this->shadows)
		ret.push_back(shadow.get_id());
	return ret;
}

VssSnapshot::VssSnapshot(){
	this->state = VssState::Initial;
	this->vbc = nullptr;
}

#define CALL_HRESULT_FUNCTION(f, params)           \
	{                                              \
		auto hres = f params;                      \
		if (FAILED(hres))                          \
			throw HresultException(#f "()", hres); \
	}

#define CALL_ASYNC_FUNCTION(f)              \
	{                                       \
		IVssAsync *async = nullptr;         \
		CALL_HRESULT_FUNCTION(f, (&async)); \
		VssAsync(async).wait();             \
	}

class VssAsync{
	IVssAsync *async;
public:
	VssAsync(IVssAsync *async) : async(async){}
	~VssAsync(){
		if (this->async)
			this->async->Release();
	}
	void wait(){
		CALL_HRESULT_FUNCTION(this->async->Wait, ());
	}
};

void VssSnapshot::begin(){
	if (this->state != VssState::Initial)
		throw IncorrectUsageException();

	this->state = VssState::Invalid;

	{
		IVssBackupComponents *temp;
		CALL_HRESULT_FUNCTION(CreateVssBackupComponents, (&temp));
		this->vbc = temp;
	}
	CALL_HRESULT_FUNCTION(this->vbc->InitializeForBackup, ());
	CALL_ASYNC_FUNCTION(this->vbc->GatherWriterMetadata);
	CALL_HRESULT_FUNCTION(this->vbc->FreeWriterMetadata, ());
	const auto context = VSS_CTX_BACKUP | VSS_CTX_CLIENT_ACCESSIBLE_WRITERS | VSS_CTX_APP_ROLLBACK;
	CALL_HRESULT_FUNCTION(this->vbc->SetContext, (context));
	VSS_ID snapshot_set_id;
	CALL_HRESULT_FUNCTION(this->vbc->StartSnapshotSet, (&snapshot_set_id));
	this->props.set_snapshot_set_id(snapshot_set_id);

	this->state = VssState::PushingTargets;
}

void VssSnapshot::push_target(const std::wstring &target){
	if (this->state != VssState::PushingTargets)
		throw IncorrectUsageException();

	this->state = VssState::Invalid;

	//Kind of lousy, but probably good enough.
	VSS_PWSZ temp = (VSS_PWSZ)target.c_str();
	VSS_ID shadow_id;
	CALL_HRESULT_FUNCTION(this->vbc->AddToSnapshotSet, (temp, GUID_NULL, &shadow_id));
	this->props.add_shadow_id(shadow_id);

	this->state = VssState::PushingTargets;
}

class RaiiSnapshotProperties{
	VSS_SNAPSHOT_PROP props;
public:
	RaiiSnapshotProperties(const VSS_SNAPSHOT_PROP &props) : props(props){}
	~RaiiSnapshotProperties(){
		VssFreeSnapshotProperties(&this->props);
	}
	VSS_SNAPSHOT_PROP get_properties() const{
		return this->props;
	}
};

void VssSnapshot::do_snapshot(HRESULT &properties_result){
	if (this->state != VssState::PushingTargets)
		throw IncorrectUsageException();

	this->state = VssState::Invalid;

	CALL_HRESULT_FUNCTION(this->vbc->SetBackupState, (false, true, VSS_BT_COPY));
	CALL_ASYNC_FUNCTION(this->vbc->PrepareForBackup);
	CALL_ASYNC_FUNCTION(this->vbc->GatherWriterStatus);
	CALL_HRESULT_FUNCTION(this->vbc->FreeWriterStatus, ());
	CALL_ASYNC_FUNCTION(this->vbc->DoSnapshotSet);

	this->state = VssState::SnapshotPerformed;

	this->populate_properties();
}

void VssSnapshot::populate_properties(){
	auto shadow_ids = this->props.get_shadow_ids();

	for (auto &shadow_id : shadow_ids){
		VSS_SNAPSHOT_PROP props;
		auto hres = this->vbc->GetSnapshotProperties(this->props.get_snapshot_set_id(), &props);
		if (FAILED(hres))
			throw HresultException("IVssBackupComponents::SnapshotProperties", hres);
		RaiiSnapshotProperties raii_props(props);

	}
}

VssSnapshot::~VssSnapshot(){
	if (this->state == VssState::SnapshotPerformed){
		CALL_ASYNC_FUNCTION(this->vbc->GatherWriterStatus);
		CALL_HRESULT_FUNCTION(this->vbc->FreeWriterStatus, ());
		CALL_ASYNC_FUNCTION(this->vbc->BackupComplete);
		LONG deleted_snapshots;
		VSS_ID undeleted;
		CALL_HRESULT_FUNCTION(
			this->vbc->DeleteSnapshots,
			(
				this->props.get_snapshot_set_id(),
				VSS_OBJECT_SNAPSHOT_SET,
				true,
				&deleted_snapshots,
				&undeleted
			)
		);
	}
	if (this->vbc)
		this->vbc->Release();
}
