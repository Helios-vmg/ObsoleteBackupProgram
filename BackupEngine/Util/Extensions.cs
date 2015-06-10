using System;
using System.Collections.Generic;

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
    }
}
