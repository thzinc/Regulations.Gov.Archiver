using System.Collections.Generic;

namespace Regulations.Gov.Downloader.Clients
{
    public class DocumentsResponse
    {
        public List<DocumentReference> Documents { get; set; } = new List<DocumentReference>();
        public long TotalNumRecords { get; set; }
    }
}
