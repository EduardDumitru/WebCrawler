using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.QueryDsl;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Search
{
    public class ElasticSearchSink : ISink
    {
        private readonly ElasticsearchClient _client;

        private static readonly string[] companyFieldNames = [
                    "companyCommercialName",
                    "companyLegalName",
                    "companyAllAvailableNames",
                        ];

        public ElasticSearchSink()
        {
            var uri = Environment.GetEnvironmentVariable("ELASTICSEARCH_URI") ?? "http://localhost:9200";
            var settings = new ElasticsearchClientSettings(new Uri(uri))
                .RequestTimeout(TimeSpan.FromSeconds(30)) // Set request timeout
                .MaximumRetries(3); // Number of retries on failure
            _client = new ElasticsearchClient(settings);
        }

        public async Task WriteCompaniesAsync(IEnumerable<CompanyExtended> results, string? destination = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                throw new ArgumentException("Destination (index name) must be provided for ElasticSearchSink.");
            }

            var indexName = destination;

            // Only create mapping for known types
            var existsResponse = await _client.Indices.ExistsAsync(indexName, cancellationToken);
            if (!existsResponse.Exists)
            {
                await _client.Indices.CreateAsync(indexName, c => c
                    .Mappings(m => m
                        .Properties<CompanyExtended>(p => p
                            .Keyword(k => k.Domain)
                            .Text(t => t.CompanyCommercialName)
                            .Text(t => t.CompanyLegalName)
                            .Text(t => t.CompanyAllAvailableNames)
                            .Text(t => t.Phones)
                            .Text(t => t.SocialLinks)
                            .Text(t => t.Addresses)
                                                    )
                             ), cancellationToken);
            }

            var bulkRequest = new BulkRequest(indexName)
            {
                Operations = []
            };

            foreach (var result in results)
            {
                bulkRequest.Operations.Add(new BulkIndexOperation<CompanyExtended>(result)
                {
                    Id = result.Domain, // Use domain as unique ID
                });
            }

            try
            {
                var bulkResponse = await _client.BulkAsync(bulkRequest, cancellationToken);
                if (bulkResponse.Errors)
                {
                    throw new Exception("Bulk indexing to ElasticSearch failed.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("ElasticSearch operation failed.", ex);
            }
        }

        public async Task<SinkSearchResponse<ScoredCompanyResult>> SearchCompaniesAsync(Core.Models.SearchRequest request, CancellationToken cancellationToken = default)
        {
            var shouldQueries = new List<Query>();

            if (!string.IsNullOrWhiteSpace(request.InputName))
            {
                shouldQueries.Add(new Query
                {
                    MultiMatch = new MultiMatchQuery
                    {
                        Fields = companyFieldNames.Select(f => f + "^2").ToArray(), // Boost name fields
                        Query = request.InputName,
                        Fuzziness = "AUTO"
                    }
                });
            }
            if (!string.IsNullOrWhiteSpace(request.InputPhone))
            {
                shouldQueries.Add(new Query
                {
                    Match = new MatchQuery
                    {
                        Field = "phones",
                        Query = request.InputPhone
                    }
                });
            }
            if (!string.IsNullOrWhiteSpace(request.InputWebsite))
            {
                shouldQueries.Add(new Query
                {
                    Term = new TermQuery
                    {
                        Field = "domain",
                        Value = request.InputWebsite.ToLowerInvariant() // normalize if needed
                    }
                });
            }
            if (!string.IsNullOrWhiteSpace(request.InputFacebook))
            {
                shouldQueries.Add(new Query
                {
                    Match = new MatchQuery
                    {
                        Field = "socialLinks",
                        Query = request.InputFacebook
                    }
                });
            }

            var response = await _client.SearchAsync<CompanyExtended>(s => s
                .Indices(ElasticSearchIndexes.Companies)
                .Query(q => q
                    .Bool(b => b
                        .Should(shouldQueries.ToArray())
                         // .MinimumShouldMatch(1) // Optional: uncomment if you want at least one match
                         )
                      )
                .Size(request.Size)
                , cancellationToken);

            var scoredResults = response.Hits
                                    .Select(hit => new ScoredCompanyResult
                                    {
                                        Company = hit.Source,
                                        Score = hit.Score
                                    }
                                           )
                                    .ToList();

            return new SinkSearchResponse<ScoredCompanyResult>
            {
                Results = scoredResults,
                Total = response.Total
            };
        }
    }
}