using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BackupEngine.Util
{

    public enum HashType
    {
        None,
        Sha1,
        Md5,
        Sha256,
        Default = Sha256,
    }

    static class Hash
    {
        public static HashAlgorithm New(HashType type)
        {
            switch (type)
            {
                case HashType.Sha1:
                    return SHA1.Create();
                case HashType.Md5:
                    return MD5.Create();
                case HashType.Sha256:
                    return SHA256.Create();
                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }
    }
}
