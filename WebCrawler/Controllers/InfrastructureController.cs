using Microsoft.AspNetCore.Mvc;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Services.Search;

namespace WebCrawler.Controllers
{
    [ApiController]
    [Route("api/infrastructure")]
    public class InfrastructureController(ISink elasticSink, ICrawlResultMerger crawlResultMerger, CompanyDataCache companyDataCache) : ControllerBase
    {
        [HttpPost(Name = "Setup")]
        [Route("setup")]
        public async Task<IActionResult> Setup([FromForm] IFormFile domainsDocument)
        {
            // 1. Save uploaded Excel/CSV file
            var filePath = "domains.csv";
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await domainsDocument.CopyToAsync(stream);
            }

            // 2. Split the file into 10 smaller files
            var splitFiles = crawlResultMerger.SplitExcelOrCsv(filePath, 10);

            return Ok(new { Message = "Infrastructure files generated.", Files = splitFiles });
        }

        [HttpPost(Name = "ReindexElasticSearch")]
        [Route("reindex-elasticsearch")]
        public async Task<IActionResult> ReindexElasticSearch()
        {
            // Get all merged results (adapt as needed for your data source)
            var mergedResults = companyDataCache.Companies;

            await elasticSink.WriteCompaniesAsync(mergedResults, ElasticSearchIndexes.Companies);

            return Ok(new { Message = "ElasticSearch reindex completed.", mergedResults.Count });
        }
    }
}