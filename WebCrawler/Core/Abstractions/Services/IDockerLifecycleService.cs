namespace WebCrawler.Core.Abstractions.Services
{
    public interface IDockerLifecycleService
    {
        Task RunCrawlAsync(string containerId, string crawlerIndex, string crawlerDoneFile, string resultsDirectory, CancellationToken cancellationToken = default);

        Task RunCombineAndMergeAsync(CancellationToken cancellationToken = default);
    }
}