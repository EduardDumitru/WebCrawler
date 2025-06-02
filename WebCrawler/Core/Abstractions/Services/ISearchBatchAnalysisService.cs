using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ISearchBatchAnalysisService
    {
        Task<string> AnalyzeBatchFromCsvAsync(
            IFormFile inputCsv,
            string outputDir = "results",
            CancellationToken cancellationToken = default);

        Task WriteAnalysisResultsAsync(IEnumerable<SearchAnalysisResult> analysisResults, string outputDir = "results", string? csvPath = null);

        Task WriteAnalysisResultAsync(SearchAnalysisResult analysisResult, string outputDir = "results", string? csvPath = null);
    }
}