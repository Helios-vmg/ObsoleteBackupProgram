using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine.Util
{
    public static class IntegerOperations
    {
        public static uint UncheckedToUint32(int i)
        {
            uint ret;
            unchecked
            {
                ret = (uint) i;
            }
            return ret;
        }
    }
}
