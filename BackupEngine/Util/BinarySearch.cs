using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine.Util
{
    public static class BinarySearch
    {
        public static int BinaryFindFirst<T>(this List<T> sequence, Func<T, bool> geqPredicate)
        {
            int begin = 0,
                end = sequence.Count;
            if (begin >= end)
                return end;
            if (geqPredicate(sequence[begin]))
                return begin;
            var diff = end - begin;
            while (diff > 1)
            {
                var pivot = begin + diff / 2;
                if (!geqPredicate(sequence[pivot]))
                    begin = pivot;
                else
                    end = pivot;
                diff = end - begin;
            }
            return end;
        }
    }
}
