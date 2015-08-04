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

        public static void ZipWith<T1, T2>(this IEnumerable<T1> xs, IEnumerable<T2> ys, Action<T1, T2> f)
        {
            using (var e1 = xs.GetEnumerator())
            using (var e2 = ys.GetEnumerator())
            {
                while (e1.MoveNext() && e2.MoveNext())
                {
                    f(e1.Current, e2.Current);
                }
            }
        }
    }
}
