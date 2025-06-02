namespace WebCrawler.Core.Models
{
    public class SinkSearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public int? Size { get; set; }
        // Add more fields as needed (e.g., filters, paging)
    }
}