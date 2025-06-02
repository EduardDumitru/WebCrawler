namespace WebCrawler.Core.Models
{
    // DTO for request
    public class SearchRequest
    {
        public string? InputName { get; set; }
        public string? InputPhone { get; set; }
        public string? InputWebsite { get; set; }
        public string? InputFacebook { get; set; }
        public int? Size { get; set; } = 10;
    }
}