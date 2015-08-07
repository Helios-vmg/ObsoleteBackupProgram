namespace test1
{
    class Program
    {
        static void Main(string[] args)
        {
            var processor = new LineProcessor(args);
            processor.Process();
        }
    }
}
