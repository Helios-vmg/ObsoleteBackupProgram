using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace busytest
{
    class Program
    {
        static void Main(string[] args)
        {
            const string path = @"g:\Backup\test\Backup.7z";
            Func<Stream> f = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using (var file = f())
            {
                using (var file2 = f())
                {
                    
                }
            }
        }
    }
}
