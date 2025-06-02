namespace WebCrawler.Core.Models
{
    public class ScoredCompanyResult
    {
        public CompanyExtended? Company { get; set; }
        public double? Score { get; set; }
    }
}