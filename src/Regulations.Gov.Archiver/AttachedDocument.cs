using System;

namespace Regulations.Gov.Archiver
{
    public class AttachedDocument
    {
        public string Content { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Keywords { get; set; }
        public DateTimeOffset Date { get; set; }
        public string ContentType { get; set; }
        public long ContentLength { get; set; }
        public string Language { get; set; }
    }
}
