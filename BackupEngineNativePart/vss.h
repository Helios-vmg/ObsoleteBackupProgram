#pragma once

class VssShadow{
	VSS_ID snapshot_set_id;
	VSS_ID id;
	long snapshots_count;
	std::wstring snapshot_device_object,
		original_volume_name,
		originating_machine,
		service_machine,
		exposed_name,
		exposed_path;
	VSS_ID provider_id;
	long snapshot_attributes;
	VSS_TIMESTAMP created_at;
	VSS_SNAPSHOT_STATE status;
public:
	VssShadow(const VSS_ID &snapshot_set_id, const VSS_ID &shadow_id):
		snapshot_set_id(snapshot_set_id),
		id(shadow_id){}
	VSS_ID get_id() const{
		return this->id;
	}
};

class SnapshotProperties{
	VSS_ID snapshot_set_id;
	std::vector<VssShadow> shadows;
public:
	SnapshotProperties();
	void set_snapshot_set_id(const VSS_ID &snapshot_set_id){
		this->snapshot_set_id = snapshot_set_id;
	}
	const VSS_ID &get_snapshot_set_id() const{
		return this->snapshot_set_id;
	}
	void add_shadow_id(const VSS_ID &shadow_id){
		this->shadows.push_back(VssShadow(this->snapshot_set_id, shadow_id));
	}
	std::vector<VSS_ID> get_shadow_ids() const;
};

class HresultException : public std::exception{
	std::string message;
public:
	HresultException(const char *context, HRESULT hres);
	const char *what() const override{
		return this->message.c_str();
	}
};

class VssSnapshot{
	enum class VssState{
		Initial,
		PushingTargets,
		SnapshotPerformed,
		CleanedUp,
		Invalid,
	};
	VssState state;
	std::vector<std::wstring> targets;
	SnapshotProperties props;
	IVssBackupComponents *vbc;

	void populate_properties();
public:
	VssSnapshot();
	~VssSnapshot();
	//Requirement: this->state == VssState::Initial
	void begin();
	//Requirement: this->state == VssState::PushingTargets
	void push_target(const std::wstring &target);
	//Requirement: this->state == VssState::PushingTargets && 
	//             this->targets.size() >= 1
	void do_snapshot(HRESULT &properties_result);
	//Requirement: this->state == VssState::SnapshotPerformed
	const SnapshotProperties &get_snapshot_properties() const{
		return this->props;
	}

	class IncorrectUsageException : public std::exception{
	public:
		const char *what() const{
			return "The object was in a state that was incorrect for the called function.";
		}
	};
};

/*
	Workflow:
	{
		VssSnapshot snapshot;
		snapshot.begin();
		//Push targets.
		//example: snapshot.push_target(L"c:\\");
		snapshot.do_snapshot();
		auto props = snapshot.get_snapshot_properties();
		//Use props to access files in snapshot.
	} //Snapshot is deleted and all data structures are freed when the object is
	  //destructed.
*/
