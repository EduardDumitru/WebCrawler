namespace WebCrawler.Core.Models
{
    public class SearchAnalysisResult
    {
        public SearchRequest? Request { get; set; }
        public List<ScoredCompanyResult>? Results { get; set; }
    }
}