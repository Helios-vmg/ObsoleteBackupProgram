#define EXPORT_THIS extern "C" _declspec(dllexport)
typedef void (*callback)(const wchar_t *);

EXPORT_THIS bool is_reparse_point(const wchar_t *_path);
EXPORT_THIS int get_reparse_point_target(const wchar_t *_path, unsigned long *unrecognized, callback f);
EXPORT_THIS int get_file_guid(const wchar_t *_path, GUID *guid);
EXPORT_THIS unsigned get_file_system_object_type(const wchar_t *_path);
EXPORT_THIS int list_all_hardlinks(const wchar_t *_path, callback f);
EXPORT_THIS int get_file_size(__int64 *dst, const wchar_t *_path);
EXPORT_THIS int create_symlink(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_directory_symlink(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_junction(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_file_reparse_point(const wchar_t *_link_location, const wchar_t *_target_location);
EXPORT_THIS int create_hardlink(const wchar_t *_link_location, const wchar_t *_existing_file);
EXPORT_THIS void test_func();
