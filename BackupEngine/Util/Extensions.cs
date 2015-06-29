using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BackupEngine.Util
{
    public static class Extensions
    {
        public static T2 GetValueOrDefault<T1, T2>(this Dictionary<T1, T2> d, T1 key, T2 def)
        {
            T2 ret;
            return d.TryGetValue(key, out ret) ? ret : def;
        }

        public static void ForEach<T>(this IEnumerable<T> e, Action<T> f)
        {
            foreach (var i in e)
                f(i);
        }

        public static IEnumerable<T> ToEnumerable<T>(this T o)
        {
            yield return o;
        }

        public static byte[] ReadAllBytes(this Stream stream)
        {
            var ret = new byte[stream.Length];
            stream.Read(ret, 0, ret.Length);
            return ret;
        }

        public static T Back<T>(this List<T> list)
        {
            return list[list.Count - 1];
        }

        public static IEnumerable<T> Sorted<T>(this IEnumerable<T> xs)
        {
            return xs.OrderBy(x => x);
        }

        public static IEnumerable<T> Reversed<T>(this IEnumerable<T> xs)
        {
            return xs.Reverse();
        }
    }
}
