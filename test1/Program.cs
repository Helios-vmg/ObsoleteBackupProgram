using System.Runtime.InteropServices;

namespace test1
{
    class Program
    {
        [DllImport("BackupEngineNativePart64.dll", CharSet = CharSet.Unicode,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern void test_func(string path);

        static void Main(string[] args)
        {
            test_func(null);
            return;
            var processor = new LineProcessor(args);
            processor.Process();
        }
    }
}
