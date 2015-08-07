using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackupEngine.Util.Streams;

namespace BackupEngine.Archive
{
    public abstract class Archive : IDisposable
    {
        protected ArchiveMetadata.CompressionMethodType CompressionMethod = ArchiveMetadata.CompressionMethodType.BZip2;
        private readonly List<FilterGenerator> _filters = new List<FilterGenerator>();

        public abstract void Dispose();

        public virtual void AddFilterGenerator(FilterGenerator fg)
        {
            _filters.Add(fg);
        }

        protected InputFilter DoFiltering(Stream stream, bool includeEncryption = true)
        {
            var ret = stream as InputFilter;
            var enumeration = includeEncryption ? _filters : _filters.Where(x => !x.IsEncryption);
            bool first = true;
            foreach (var filterGenerator in enumeration)
            {
                ret = filterGenerator.Filter(ret ?? stream, ret == null && first);
                first = false;
            }
            return ret ?? new IdentityInputFilter(stream);
        }
    }
}
