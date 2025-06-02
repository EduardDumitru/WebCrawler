using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Docker
{
    public class DockerLifecycleService(ICrawlService crawlService, ICrawlResultWriter crawlResultWriter, ICrawlResultMerger crawlResultMerger) : IDockerLifecycleService
    {
        public async Task RunCrawlAsync(string containerId, string crawlerIndex, string crawlerDoneFile, string resultsDirectory, CancellationToken cancellationToken = default)
        {
            await crawlService.CrawlWebsitesAsync(containerId, cancellationToken);
            // At the end of CrawlWebsitesAsync or just before the process exits
            var doneFile = !string.IsNullOrWhiteSpace(crawlerDoneFile) ? crawlerDoneFile ?? $"crawler{crawlerIndex}.done" : string.Empty;
            if (string.IsNullOrWhiteSpace(doneFile))
            {
                throw new InvalidOperationException("Crawler done file name cannot be empty.");
            }

            if (!Directory.Exists(resultsDirectory))
            {
                Directory.CreateDirectory(resultsDirectory);
            }

            var doneFilePath = Path.Combine(resultsDirectory, doneFile);
            File.WriteAllText(doneFilePath, "done");
            return; // Exit after crawling
        }

        public async Task RunCombineAndMergeAsync(CancellationToken cancellationToken = default)
        {
            var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
            RemoveCrawlerDoneFiles(resultsDir);

            await CombineCsvResults(cancellationToken);

            var mergedResults = await MergeAndWriteResults(cancellationToken);

            await WriteResultsToElasticSearch(mergedResults, cancellationToken);

            CleanupContainerResultFiles(resultsDir);
        }

        private static void RemoveCrawlerDoneFiles(string resultsDir)
        {
            if (!Directory.Exists(resultsDir)) return;
            foreach (var file in Directory.GetFiles(resultsDir, "crawler*.done"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        private async Task CombineCsvResults(CancellationToken cancellationToken = default)
        {
            await crawlResultWriter.CombineDomainCSVs(cancellationToken: cancellationToken);
            await crawlResultWriter.CombineCoverageCSVs(cancellationToken: cancellationToken);
            await crawlResultWriter.CombineFillRatesCSVs(cancellationToken: cancellationToken);
        }

        private async Task<List<CompanyExtended>> MergeAndWriteResults(CancellationToken cancellationToken = default)
        {
            var mergedResults = await crawlResultMerger.GetMergedResults(
                "results/crawl-results.csv",
                "results/sample-websites-company-names.csv",
                cancellationToken
                                                                        );
            await crawlResultMerger.WriteMergedResultsToCsv(mergedResults, "results/Companies-Data.csv", cancellationToken);
            return mergedResults;
        }

        private async Task WriteResultsToElasticSearch(List<CompanyExtended> mergedResults, CancellationToken cancellationToken = default)
        {
            await crawlResultMerger.WriteMergedResultsToDataSink(mergedResults, cancellationToken);
        }

        private static void CleanupContainerResultFiles(string resultsDir)
        {
            DeleteFilesByPattern(resultsDir, "crawl-results-container-*.csv");
            DeleteFilesByPattern(resultsDir, "crawl-coverage-container-*.csv");
            DeleteFilesByPattern(resultsDir, "crawl-fillrates-container-*.csv");
        }

        private static void DeleteFilesByPattern(string dir, string pattern)
        {
            foreach (var file in Directory.GetFiles(dir, pattern))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }
}