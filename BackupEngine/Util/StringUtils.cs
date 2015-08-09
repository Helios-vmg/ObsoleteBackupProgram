using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        public static string RecomposePath(this IEnumerable<string> path)
        {
            var ret = new StringBuilder();
            foreach (var part in path)
            {
                if (ret.Length != 0)
                    ret.Append('\\');
                ret.Append(part);
            }
            return ret.ToString();
        }

        private static bool NeedsNormalization(string path)
        {
            if (path == null)
                return false;
            int skip = 0;
            if (path.StartsWith(@"\\?\"))
                skip = 4;
            var lastWasSlash = false;
            foreach (var c in path.Skip(skip))
            {
                if (c == '/')
                    return true;
                if (c == '\\')
                {
                    if (lastWasSlash)
                        return true;
                    lastWasSlash = true;
                }
                else
                    lastWasSlash = false;
            }
            return lastWasSlash;
        }

        public static string EnsureLastCharacterIsNotBackslash(this string path)
        {
            while (path.Length > 0 && path[path.Length - 1] == '\\')
                path = path.Substring(0, path.Length - 1);
            return path;
        }

        //Only normalizes path separators to backslash, removes consecutive
        //path separators, and ensures that the last character is not a
        //backslash.
        public static string NormalizePath(this string path)
        {
            if (!NeedsNormalization(path))
                return path;
            var lastWasSlash = false;
            var ret = new StringBuilder(path.Length);
            int skip = 0;
            if (path.StartsWith(@"\\?\"))
            {
                ret.Append(@"\\?\");
                skip = 4;
            }
            foreach (var c in path.Skip(skip))
            {
                if (c == '/' || c == '\\')
                {
                    if (lastWasSlash)
                        continue;
                    ret.Append('\\');
                    lastWasSlash = true;
                }
                else
                {
                    ret.Append(c);
                    lastWasSlash = false;
                }
            }
            if (lastWasSlash)
                ret.Length--;
            return ret.ToString();
        }

        public static string SimplifyPath(this string path)
        {
            return path.NormalizePath().ToLower();
        }

        public static bool PathMatch(this string a, string b)
        {
            return a.DecomposePath().PathMatch(b.DecomposePath());
        }

        public static bool PathMatch(this IEnumerable<string> pathA, IEnumerable<string> pathB)
        {
            var arrayA = pathA.ToArray();
            var arrayB = pathB.ToArray();
            if (arrayA.Length != arrayB.Length)
                return false;
            for (int i = arrayA.Length; i-- != 0;)
                if (!arrayA[i].Equals(arrayB[i], StringComparison.CurrentCultureIgnoreCase))
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
