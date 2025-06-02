using System.Collections.Concurrent;
using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ICrawlResultWriter
    {
        public Task SaveResultsToCsv(IList<string> domains, ConcurrentBag<CrawlResult> results, string containerId, string resultsDir = "results", CancellationToken cancellationToken = default);

        Task CombineDomainCSVs(string resultsDir = "results", CancellationToken cancellationToken = default);

        Task CombineCoverageCSVs(string resultsDir = "results", CancellationToken cancellationToken = default);

        Task CombineFillRatesCSVs(string resultsDir = "results", CancellationToken cancellationToken = default);
    }
}