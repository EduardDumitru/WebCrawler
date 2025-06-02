namespace WebCrawler.Core.Abstractions.Services
{
    public interface ICrawlService
    {
        Task<IList<string>> GetDomains(CancellationToken cancellationToken = default);

        Task CrawlWebsitesAsync(string containerId, CancellationToken cancellationToken = default);
    }
}