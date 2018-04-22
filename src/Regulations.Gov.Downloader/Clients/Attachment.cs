using System.Collections.Generic;

namespace Regulations.Gov.Downloader.Clients
{
    public class Attachment
    {
        public int AttachmentOrderNumber { get; set; }
        public List<string> FileFormats { get; set; } = new List<string>();
    }
}
