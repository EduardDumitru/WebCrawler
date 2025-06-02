using Microsoft.AspNetCore.Mvc;
using WebCrawler.Services.Crawling;

namespace WebCrawler.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CrawlController(CrawlService crawlService) : ControllerBase
    {
        [HttpGet(Name = "GetDomains")]
        [Route("GetDomains")]
        public async Task<IEnumerable<string>> Get(CancellationToken cancellationToken = default)
        {
            return await crawlService.GetDomains(cancellationToken);
        }
    }
}