using Regulations.Gov.Client;

namespace Regulations.Gov.Archiver
{
    public class Document : DocumentStub
    {
        /// <summary>
        /// Index of the document when ordered by PostedDate ascending
        /// </summary>
        public int? IndexByPostedDate { get; set; }
        public string Fingerprint { get; set; }
        public virtual string Id => $"{DocumentId}{(DocumentIdType == null ? "" : $"-{DocumentIdType}")}{(AttachmentNumber.HasValue ? $"-{AttachmentNumber}" : "")}";
        public string DocumentIdType { get; set; }
        public string ReportedContentType { get; set; }
        public string ReportedFilename { get; set; }
        /// <summary>
        /// Contents of the attachment as base64-encoded string
        /// </summary>
        public string Data { get; set; }
        public AttachedDocument Attachment { get; set; }
        public int? AttachmentNumber { get; set; }


    }
}
