using Microsoft.AspNetCore.Mvc;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Helpers;

namespace WebCrawler.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SearchController(ISearchService searchService, ISearchBatchAnalysisService batchAnalysisService) : ControllerBase
    {
        [HttpPost(Name = "FindBestMatchInCSV")]
        [Route("FindBestMatchInCsv")]
        public async Task<IActionResult> FindBestMatchInCSV([FromBody] SearchRequest request)
        {
            var best = searchService.FindCompanyMatchInCsv(request);

            try
            {
                await batchAnalysisService.WriteAnalysisResultAsync(new SearchAnalysisResult { Request = request, Results = best == null ? [] : [best] });
            }
            catch (Exception ex)
            {
                LoggerHelper.LogToFile($"Error writing analysis result: {ex.Message}");
            }

            if (best == null)
                return NotFound();

            return Ok(new { best.Company, best.Score });
        }

        [HttpPost(Name = "FindBestMatchInElasticSearch")]
        [Route("FindBestMatchInElasticSearch")]
        public async Task<IActionResult> FindBestMatchInElasticSearch([FromBody] SearchRequest request, CancellationToken cancellationToken = default)
        {
            var matchedResults = await searchService.FindCompanyMatchInElasticSearch(request, cancellationToken);

            try
            {
                await batchAnalysisService.WriteAnalysisResultAsync(new SearchAnalysisResult { Request = request, Results = matchedResults?.Results?.ToList() ?? new List<ScoredCompanyResult>() });
            }
            catch (Exception ex)
            {
                LoggerHelper.LogToFile($"Error writing analysis result: {ex.Message}");
            }

            if (matchedResults == null)
                return NotFound();

            return Ok(matchedResults);
        }

        [HttpPost(Name = "BatchCsvAnalysis")]
        [Route("BatchCsvAnalysis")]
        public async Task<IActionResult> BatchCsvAnalysis([FromForm] IFormFile inputCsv, CancellationToken cancellationToken = default)
        {
            if (inputCsv == null || inputCsv.Length == 0)
                return BadRequest("No CSV file uploaded.");

            var csvPath = await batchAnalysisService.AnalyzeBatchFromCsvAsync(inputCsv, "results", cancellationToken);

            return Ok(new { Message = "Batch analysis complete.", CsvPath = csvPath });
        }
    }
}