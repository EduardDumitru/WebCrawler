namespace WebCrawler.Core.Models
{
    public class SinkSearchResponse<T>
    {
        public IReadOnlyCollection<T> Results { get; set; } = [];
        public long? Total { get; set; }
    }
}