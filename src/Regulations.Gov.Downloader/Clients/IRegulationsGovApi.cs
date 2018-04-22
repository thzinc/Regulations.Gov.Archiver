using System.Threading.Tasks;
using RestEase;
using System.IO;

namespace Regulations.Gov.Downloader.Clients
{
    [AllowAnyStatusCode]
    public interface IRegulationsGovApi
    {
        [Query("api_key")]
        string ApiKey { get; set; }

        [Get("/regulations/v3/documents.json?rpp=1000")]
        Task<Response<DocumentsResponse>> GetRecentDocumentsAsync(int daysSinceModified, int po);

        [Get("/regulations/v3/documents.json?rpp=1000&sb=postedDate&so=ASC")]
        Task<Response<DocumentsResponse>> GetDocumentsAsync(int po);

        [Get("/regulations/v3/document.json")]
        Task<Response<Document>> GetDocumentAsync(string documentId);

        [Get("{url}")]
        Task<Response<Stream>> GetDownloadAsync([Path(UrlEncode = false)]string url);
    }
}
