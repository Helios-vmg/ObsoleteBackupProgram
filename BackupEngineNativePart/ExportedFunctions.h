#define EXPORT_THIS extern "C" _declspec(dllexport)
typedef void (*callback)(const wchar_t *);

struct GUID;

bool is_reparse_point(const wchar_t *_path);
int get_reparse_point_target(const wchar_t *_path, unsigned long *unrecognized, callback f);
int get_file_guid(const wchar_t *_path, GUID *guid);
unsigned get_file_system_object_type(const wchar_t *_path);
int list_all_hardlinks(const wchar_t *_path, callback f);
int get_file_size(__int64 *dst, const wchar_t *_path);
