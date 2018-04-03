using Regulations.Gov.Client;

namespace Regulations.Gov.Archiver
{
    public class DocumentsQueryResult
    {
        public DocumentsQuery Query { get; set; }
        public DocumentsResult Result { get; set; }
    }
}
