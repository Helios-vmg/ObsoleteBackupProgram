using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        protected Stream DoFiltering(Stream stream, bool includeEncryption = true)
        {
            bool first = true;
            var enumeration = includeEncryption ? _filters : _filters.Where(x => !x.IsEncryption);
            foreach (var filterGenerator in enumeration)
            {
                stream = filterGenerator.Filter(stream, first);
                first = false;
            }
            return stream;
        }
    }
}
