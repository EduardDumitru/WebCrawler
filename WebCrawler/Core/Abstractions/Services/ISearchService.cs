using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ISearchService
    {
        public ScoredCompanyResult? FindCompanyMatchInCsv(SearchRequest request);

        public Task<SinkSearchResponse<ScoredCompanyResult>> FindCompanyMatchInElasticSearch(SearchRequest request, CancellationToken cancellationToken = default);
    }
}