using System;

namespace BackupEngine
{
    public interface IErrorReporter
    {
        //If true, the error will be handled and the program will continue.
        //Otherwise, the exception will be rethrown.
        bool ReportError(Exception e, string context = null);
    }
}
