namespace Regulations.Gov.Downloader.Actors
{
    public class PersistFile
    {
        public string DocumentId { get; set; }
        public byte[] Bytes { get; set; }
        public string Tag { get; set; }
        public string ContentType { get; set; }
        public string OriginalFileName { get; set; }
    }
}
