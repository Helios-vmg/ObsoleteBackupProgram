using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine
{
    public interface IErrorReporter
    {
        //If true, the will be handled and the program will continue.
        //Otherwise, the exception will be rethrown.
        bool ReportError(Exception e, string context = null);
    }
}
