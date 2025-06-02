using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ISink
    {
        Task WriteCompaniesAsync(IEnumerable<CompanyExtended> results, string? destination = null, CancellationToken cancellationToken = default);

        Task<SinkSearchResponse<ScoredCompanyResult>> SearchCompaniesAsync(SearchRequest request, CancellationToken cancellationToken = default);
    }
}