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
            var path = args[0];
            Func<Stream> f = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using (var file = f())
            {
                Console.WriteLine("File " + path + " locked.");
                Console.ReadLine();
            }
        }
    }
}
