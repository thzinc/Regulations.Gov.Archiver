using System;
using Nest;

namespace Regulations.Gov.Ingester.Actors
{
    public abstract class FileContent
    {
        public virtual string Id { get; set; }
        public string DocumentId { get; set; }
        [Ignore]
        public byte[] Content { get; set; } = new byte[0];
        public string MimeType { get; set; }
        public string Source { get; set; }
        public string SourceId { get; set; }
        public DateTimeOffset? CreatedTime { get; set; }
        public DateTimeOffset? ModifiedTime { get; set; }
    }
}
