using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ICrawlResultMerger
    {
        Task<List<CompanyExtended>> GetMergedResults(string crawlResultsPath, string companyNamesPath, CancellationToken cancellationToken = default);

        Task WriteMergedResultsToCsv(IEnumerable<CompanyExtended> mergedResults, string outputCsvPath, CancellationToken cancellationToken = default);

        Task WriteMergedResultsToDataSink(IEnumerable<CompanyExtended> mergedResults, CancellationToken cancellationToken = default);

        List<string> SplitExcelOrCsv(string filePath, int splitCount);
    }
}