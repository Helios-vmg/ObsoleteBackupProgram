using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine.Util
{
    public static class StringUtils
    {
        public static void SplitIntoObjectAndContainer(this string path, out string container, out string name)
        {
            if (path == null)
                path = string.Empty;
            var index = path.LastIndexOf('\\');
            while (index == path.Length - 1)
            {
                path = path.Substring(0, path.Length - 1);
                index = path.LastIndexOf('\\');
            }
            if (index < 0 || index >= path.Length)
            {
                container = string.Empty;
                name = path;
                return;
            }
            container = path.Substring(0, index);
            name = path.Substring(index + 1);
        }

        public static string GetNameFromPath(this string path)
        {
            string name;
            string container;
            path.SplitIntoObjectAndContainer(out container, out name);
            return name;
        }

        public static bool PathMatch(this string a, string b)
        {
            return a.DecomposePath().PathMatch(b.DecomposePath());
        }

        public static bool PathMatch(this IEnumerable<string> a, IEnumerable<string> b)
        {
            var A = a.ToArray();
            var B = b.ToArray();
            if (A.Length != B.Length)
                return false;
            for (int i = A.Length; i-- != 0;)
                if (!A[i].Equals(B[i], StringComparison.CurrentCultureIgnoreCase))
                    return false;
            return true;
        }

        private static readonly char[] PathSplitters = {'\\', '/'};

        public static IEnumerable<string> DecomposePath(this string path)
        {
            return path.Split(PathSplitters, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool PathStartsWith(this string thisPath, IEnumerable<string> otherPath)
        {
            return thisPath.DecomposePath().PathStartsWith(otherPath);
        }

        public static bool PathStartsWith(this IEnumerable<string> thisPath, IEnumerable<string> otherPath)
        {
            var enumerator = thisPath.GetEnumerator();
            return otherPath.All(s => enumerator.MoveNext() && PathMatch(enumerator.Current, s));
        }
    }
}
