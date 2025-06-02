namespace WebCrawler.Core.Models
{
    public class CrawlResult
    {
        public string Domain { get; set; } = string.Empty;
        public IList<string> Phones { get; set; } = [];
        public IList<string> SocialLinks { get; set; } = [];
        public IList<string> Addresses { get; set; } = [];
    }
}