namespace WebCrawler.Core.Models
{
    public class CompanyExtended : Company
    {
        public string CompanyCommercialName { get; set; } = string.Empty;
        public string CompanyLegalName { get; set; } = string.Empty;
        public string CompanyAllAvailableNames { get; set; } = string.Empty;
    }
}