using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Helpers;

namespace WebCrawler.Services.Search
{
    public class SearchService(CompanyDataCache companyDataCache, ISinkFactory sink) : ISearchService
    {
        private static readonly Dictionary<string, double> FieldWeights = new()
        {
            ["Name"] = 0.5,
            ["Website"] = 0.3,
            ["Phone"] = 0.15,
            ["Facebook"] = 0.05
        };

        public ScoredCompanyResult? FindCompanyMatchInCsv(SearchRequest request)
        {
            var best = companyDataCache.Companies
                .AsParallel()
                .Select(c =>
                {
                    var fieldScores = new Dictionary<string, int>();

                    int nameScore = GetNameScore(request, c);
                    if (nameScore >= 0) fieldScores["Name"] = nameScore;

                    int phoneScore = GetPhoneScore(request, c);
                    if (phoneScore >= 0) fieldScores["Phone"] = phoneScore;

                    int websiteScore = GetWebsiteScore(request, c);
                    if (websiteScore >= 0) fieldScores["Website"] = websiteScore;

                    int facebookScore = GetFacebookScore(request, c);
                    if (facebookScore >= 0) fieldScores["Facebook"] = facebookScore;

                    // Only use weights for present fields
                    double totalWeight = fieldScores.Keys.Sum(k => FieldWeights[k]);
                    double weightedScore = totalWeight > 0
                        ? fieldScores.Sum(kv => kv.Value / 100.0 * FieldWeights[kv.Key]) / totalWeight
                        : 0.0;

                    return new ScoredCompanyResult { Company = c, Score = weightedScore };
                })
                .Where(x => x.Score >= 0.650)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            return best;
        }

        public async Task<SinkSearchResponse<ScoredCompanyResult>> FindCompanyMatchInElasticSearch(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return await sink.GetSink(SinkType.ElasticSearch).SearchCompaniesAsync(request, cancellationToken);
        }

        private static int GetNameScore(SearchRequest request, CompanyExtended c)
        {
            if (string.IsNullOrWhiteSpace(request.InputName))
                return -1;

            var normalizedInputName = SearchHelper.NormalizeName(request.InputName);
            var nameScores = new List<int>
            {
                SearchHelper.MaxFuzzy(normalizedInputName, [SearchHelper.NormalizeName(c.CompanyCommercialName)]),
                SearchHelper.MaxFuzzy(normalizedInputName, [SearchHelper.NormalizeName(c.CompanyLegalName)]),
                SearchHelper.MaxFuzzyPipeOrSingle(normalizedInputName, SearchHelper.NormalizeName(c.CompanyAllAvailableNames))
            };
            return nameScores.Max();
        }

        private static int GetPhoneScore(SearchRequest request, CompanyExtended c)
        {
            if (string.IsNullOrWhiteSpace(request.InputPhone))
                return -1;

            var normalizedInputPhones = request.InputPhone
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(SearchHelper.NormalizePhoneVariants)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            if (normalizedInputPhones.Count == 0)
                return -1;

            var normalizedCandidatePhones = (c.Phones ?? "")
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(SearchHelper.NormalizePhoneVariants)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            return normalizedInputPhones
                .Select(inputPhone => SearchHelper.MaxFuzzy(inputPhone, normalizedCandidatePhones))
                .DefaultIfEmpty(0)
                .Max();
        }

        private static int GetWebsiteScore(SearchRequest request, CompanyExtended c)
        {
            if (string.IsNullOrWhiteSpace(request.InputWebsite))
                return -1;

            var normalizedInputWebsite = SearchHelper.NormalizeUrl(request.InputWebsite);
            var normalizedDomain = SearchHelper.NormalizeUrl(c.Domain);

            // Optionally, add normalized social links here if you want to match against them as well
            return SearchHelper.MaxFuzzy(normalizedInputWebsite, new[] { normalizedDomain, SearchHelper.NormalizeName(c.CompanyCommercialName) });
        }

        private static int GetFacebookScore(SearchRequest request, CompanyExtended c)
        {
            if (string.IsNullOrWhiteSpace(request.InputFacebook))
                return -1;

            return SearchHelper.MaxFuzzyPipeOrSingle(request.InputFacebook, c.SocialLinks);
        }
    }
}