using System.Collections.Generic;

namespace Regulations.Gov.Downloader.Clients
{
    public class Document
    {
        public List<Attachment> Attachments { get; set; } = new List<Attachment>();
        public List<string> FileFormats { get; set; } = new List<string>();
    }
}
