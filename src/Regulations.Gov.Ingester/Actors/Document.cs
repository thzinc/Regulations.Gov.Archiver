using System.Text;

namespace Regulations.Gov.Ingester.Actors
{
    public class Document : FileContent
    {
        public override string Id => DocumentId;
        public string Json => Encoding.UTF8.GetString(Content);
    }
}
