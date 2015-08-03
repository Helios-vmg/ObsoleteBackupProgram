#include "stdafx.h"
#include "vss.h"
#include "ExportedFunctions.h"
#include <comdef.h>

HresultException::HresultException(const char *context, HRESULT hres):
		hres(hres){
	std::stringstream stream;
	stream << context << " failed with error 0x"
		<< std::hex << std::setw(8) << std::setfill('0') << hres
		<< " (";
	{
		_com_error error(hres);
		for (auto p = error.ErrorMessage(); *p; p++)
			stream << ((unsigned)*p < 0x80 ? (char)*p : '?');
	}
	stream << ")";
	std::cerr << stream.str() << std::endl;
}

SnapshotProperties::SnapshotProperties(){
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

std::wstring to_wstring(const VSS_ID &guid){
	LPOLESTR temp;
	auto hres = StringFromCLSID(guid, &temp);
	std::wstring ret = temp;
	CoTaskMemFree(temp);
	return ret;
}

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
	const auto context = VSS_CTX_APP_ROLLBACK;
	CALL_HRESULT_FUNCTION(this->vbc->SetContext, (context));
	VSS_ID snapshot_set_id;
	CALL_HRESULT_FUNCTION(this->vbc->StartSnapshotSet, (&snapshot_set_id));
	this->props.set_snapshot_set_id(snapshot_set_id);

	this->state = VssState::PushingTargets;
}

HRESULT VssSnapshot::push_target(const std::wstring &target){
	if (this->state != VssState::PushingTargets)
		throw IncorrectUsageException();

	this->state = VssState::Invalid;

	//Kind of lousy, but probably good enough.
	VSS_PWSZ temp = (VSS_PWSZ)target.c_str();
	VSS_ID shadow_id;
	auto error = this->vbc->AddToSnapshotSet(temp, GUID_NULL, &shadow_id);
	if (FAILED(error)){
		if (error == VSS_E_UNEXPECTED_PROVIDER_ERROR || error == VSS_E_NESTED_VOLUME_LIMIT)
			this->state = VssState::PushingTargets;
		//throw HresultException("this->vbc->AddToSnapshotSet", error);
		return error;
	}
	this->props.add_shadow_id(shadow_id);

	this->state = VssState::PushingTargets;
	return S_OK;
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

	properties_result = this->populate_properties();
}

static std::wstring to_wstring(const VSS_PWSZ &s){
	return !s ? std::wstring() : s;
}

HRESULT VssSnapshot::populate_properties(){
	auto &shadows = this->props.get_shadows();

	for (auto &shadow : shadows){
		VSS_SNAPSHOT_PROP props;
		auto hres = this->vbc->GetSnapshotProperties(shadow.get_id(), &props);
		if (FAILED(hres))
			return hres;
		RaiiSnapshotProperties raii_props(props);
		shadow.snapshots_count        = props.m_lSnapshotsCount;
		shadow.snapshot_device_object = to_wstring(props.m_pwszSnapshotDeviceObject);
		shadow.original_volume_name   = to_wstring(props.m_pwszOriginalVolumeName);
		shadow.originating_machine    = to_wstring(props.m_pwszOriginatingMachine);
		shadow.service_machine        = to_wstring(props.m_pwszServiceMachine);
		shadow.exposed_name           = to_wstring(props.m_pwszExposedName);
		shadow.exposed_path           = to_wstring(props.m_pwszExposedPath);
		shadow.provider_id            = props.m_ProviderId;
		shadow.snapshot_attributes    = props.m_lSnapshotAttributes;
		shadow.created_at             = props.m_tsCreationTimestamp;
		shadow.status                 = props.m_eStatus;
	}
	return S_OK;
}

VssSnapshot::~VssSnapshot(){
	if (this->state == VssState::SnapshotPerformed){
		try{
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
		}catch (HresultException &){
		}
	}
	if (this->vbc)
		this->vbc->Release();
}

EXPORT_THIS int create_snapshot(void **object){
	VssSnapshot *ret;
	try{
		ret = new VssSnapshot;
		ret->begin();
	}catch (HresultException &hres){
		return hres.hres;
	}
	*object = ret;
	return S_OK;
}

EXPORT_THIS int add_volume_to_snapshot(void *object, const wchar_t *volume){
	VssSnapshot *snapshot = (VssSnapshot *)object;
	return snapshot->push_target(volume);
}

EXPORT_THIS int do_snapshot(void *object){
	VssSnapshot *snapshot = (VssSnapshot *)object;
	HRESULT properties_result;
	try{
		snapshot->do_snapshot(properties_result);
	} catch (HresultException &hres){
		return hres.hres;
	}
	return properties_result;
}

EXPORT_THIS void get_snapshot_properties(void *object, GUID *snapshot_id, get_snapshot_properties_callback callback){
	VssSnapshot *snapshot = (VssSnapshot *)object;
	auto props = snapshot->get_snapshot_properties();
	*snapshot_id = props.get_snapshot_set_id();
	auto &shadows = props.get_shadows();
	for (auto &shadow : shadows){
		callback(
			shadow.get_id(),
			shadow.snapshots_count,
			shadow.snapshot_device_object.c_str(),
			shadow.original_volume_name.c_str(),
			shadow.originating_machine.c_str(),
			shadow.service_machine.c_str(),
			shadow.exposed_name.c_str(),
			shadow.exposed_path.c_str(),
			shadow.provider_id,
			shadow.snapshot_attributes,
			shadow.created_at,
			shadow.status
		);
	}
}

EXPORT_THIS int release_snapshot(void *object){
	VssSnapshot *snapshot = (VssSnapshot *)object;
	delete snapshot;
	return 0;
}
