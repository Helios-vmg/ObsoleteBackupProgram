#define EXPORT_THIS extern "C" _declspec(dllexport)
typedef void (*string_callback_t)(const wchar_t *);

EXPORT_THIS bool is_reparse_point(const wchar_t *_path);
EXPORT_THIS int get_reparse_point_target(const wchar_t *_path, unsigned long *unrecognized, string_callback_t f);
EXPORT_THIS int get_file_guid(const wchar_t *_path, GUID *guid);
EXPORT_THIS unsigned get_file_system_object_type(const wchar_t *_path);
EXPORT_THIS int list_all_hardlinks(const wchar_t *_path, string_callback_t f);
EXPORT_THIS int get_file_size(__int64 *dst, const wchar_t *_path);
EXPORT_THIS int create_symlink(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_directory_symlink(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_junction(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_file_reparse_point(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_hardlink(const wchar_t *_link_location, const wchar_t *_existing_file);
EXPORT_THIS void test_func(const wchar_t *path);
EXPORT_THIS int create_snapshot(void **object);
EXPORT_THIS int add_volume_to_snapshot(void *object, const wchar_t *volume);
EXPORT_THIS int do_snapshot(void *object);
typedef void(*get_snapshot_properties_callback)(
	GUID shadow_id,
	int snapshots_count,
	const wchar_t *snapshot_device_object,
	const wchar_t *original_volume_name,
	const wchar_t *originating_machine,
	const wchar_t *service_machine,
	const wchar_t *exposed_name,
	const wchar_t *exposed_path,
	GUID provider_id,
	int snapshot_attributes,
	long long created_at,
	int status
);
EXPORT_THIS void get_snapshot_properties(void *object, GUID *snapshot_id, get_snapshot_properties_callback callback);
EXPORT_THIS int release_snapshot(void *object);
typedef void(*enumerate_volumes_callback_t)(const wchar_t *volume_path, const wchar_t *volume_label, unsigned drive_type);
EXPORT_THIS int enumerate_volumes(enumerate_volumes_callback_t);
EXPORT_THIS int enumerate_mounted_paths(const wchar_t *volume_path, string_callback_t cb);
