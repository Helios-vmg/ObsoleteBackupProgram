using System;
using System.Text;
using BackupEngine;

namespace test1
{
    class ErrorReporter : IErrorReporter
    {
        public bool ReportError(Exception e, string context = null)
        {
            var sb = new StringBuilder();
            sb.Append("Exception was thrown");
            if (!string.IsNullOrEmpty(context))
            {
                sb.Append(" while ");
                sb.Append(context);
            }
            sb.Append(": ");
            sb.Append(e.Message);
            Console.WriteLine(sb.ToString());
            return true;
        }
    }
}
