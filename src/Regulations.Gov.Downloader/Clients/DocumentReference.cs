using System.Collections.Generic;

namespace Regulations.Gov.Downloader.Clients
{
    public class DocumentReference : Dictionary<string, object>
    {
        public string DocumentId => (string)this["documentId"];
    }
}
