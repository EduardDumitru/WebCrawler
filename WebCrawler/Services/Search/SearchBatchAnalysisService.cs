using System.Globalization;
using CsvHelper;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Search
{
    public class SearchBatchAnalysisService(ISearchService searchService) : ISearchBatchAnalysisService
    {
        public async Task<string> AnalyzeBatchFromCsvAsync(
            IFormFile inputCsv,
            string outputDir = "results",
            CancellationToken cancellationToken = default)
        {
            if (inputCsv == null || inputCsv.Length == 0)
                throw new ArgumentException("No CSV file uploaded.");

            var requests = new List<SearchRequest>();

            // Parse the uploaded CSV into SearchRequest objects
            using (var stream = inputCsv.OpenReadStream())
            using (var reader = new StreamReader(stream))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                await csv.ReadAsync();
                csv.ReadHeader();
                while (await csv.ReadAsync())
                {
                    var req = new SearchRequest
                    {
                        InputName = csv.GetField("input name") ?? csv.GetField("input_name"),
                        InputPhone = csv.GetField("input phone") ?? csv.GetField("input_phone"),
                        InputWebsite = csv.GetField("input website") ?? csv.GetField("input_website"),
                        InputFacebook = csv.GetField("input_facebook")
                    };
                    if (string.IsNullOrWhiteSpace(req.InputName) &&
                        string.IsNullOrWhiteSpace(req.InputPhone) &&
                        string.IsNullOrWhiteSpace(req.InputWebsite) &&
                        string.IsNullOrWhiteSpace(req.InputFacebook))
                        continue;

                    requests.Add(req);
                }
            }

            Directory.CreateDirectory(outputDir);
            var csvPath = Path.Combine(outputDir, $"search-analysis-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

            // Gather analysis results
            var analysisResults = new List<SearchAnalysisResult>();
            foreach (var req in requests)
            {
                var result = await searchService.FindCompanyMatchInElasticSearch(req, cancellationToken);
                analysisResults.Add(new SearchAnalysisResult { Request = req, Results = result.Results?.ToList() ?? new List<ScoredCompanyResult>() });
            }

            // Write results to CSV using the extracted method
            await WriteAnalysisResultsAsync(analysisResults, outputDir, csvPath);

            return csvPath;
        }

        public async Task WriteAnalysisResultsAsync(IEnumerable<SearchAnalysisResult> analysisResults, string outputDir = "results", string? csvPath = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            csvPath ??= Path.Combine(outputDir, $"search-analysis-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

            using var writer = new StreamWriter(csvPath);
            using var outCsv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Write header
            outCsv.WriteField("InputName");
            outCsv.WriteField("InputWebsite");
            outCsv.WriteField("InputPhone");
            outCsv.WriteField("InputFacebook");
            outCsv.WriteField("MatchedDomain");
            outCsv.WriteField("MatchedCompanyCommercialName");
            outCsv.WriteField("MatchedCompanyLegalName");
            outCsv.WriteField("MatchedCompanyAllAvailableNames");
            outCsv.WriteField("MatchedPhones");
            outCsv.WriteField("MatchedSocialLinks");
            outCsv.WriteField("MatchedAddresses");
            outCsv.WriteField("MatchedScore");
            await outCsv.NextRecordAsync();

            foreach (var analysisResult in analysisResults)
            {
                await WriteCompanyToCSV(outCsv, analysisResult);
            }
        }

        public async Task WriteAnalysisResultAsync(SearchAnalysisResult analysisResult, string outputDir = "results", string? csvPath = null)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            csvPath ??= Path.Combine(outputDir, $"search-analysis-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");

            using var writer = new StreamWriter(csvPath);
            using var outCsv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            // Write header
            outCsv.WriteField("InputName");
            outCsv.WriteField("InputWebsite");
            outCsv.WriteField("InputPhone");
            outCsv.WriteField("InputFacebook");
            outCsv.WriteField("MatchedDomain");
            outCsv.WriteField("MatchedCompanyCommercialName");
            outCsv.WriteField("MatchedCompanyLegalName");
            outCsv.WriteField("MatchedCompanyAllAvailableNames");
            outCsv.WriteField("MatchedPhones");
            outCsv.WriteField("MatchedSocialLinks");
            outCsv.WriteField("MatchedAddresses");
            outCsv.WriteField("MatchedScore");
            await outCsv.NextRecordAsync();

            await WriteCompanyToCSV(outCsv, analysisResult);
        }

        private static async Task WriteCompanyToCSV(CsvWriter outCsv, SearchAnalysisResult analysisResult)
        {
            if (analysisResult.Results != null && analysisResult.Results.Count != 0)
            {
                foreach (var match in analysisResult.Results)
                {
                    var c = match.Company;
                    outCsv.WriteField(analysisResult.Request?.InputName);
                    outCsv.WriteField(analysisResult.Request?.InputWebsite);
                    outCsv.WriteField(analysisResult.Request?.InputPhone);
                    outCsv.WriteField(analysisResult.Request?.InputFacebook);
                    outCsv.WriteField(c?.Domain ?? "");
                    outCsv.WriteField(c?.CompanyCommercialName ?? "");
                    outCsv.WriteField(c?.CompanyLegalName ?? "");
                    outCsv.WriteField(c?.CompanyAllAvailableNames ?? "");
                    outCsv.WriteField(c?.Phones ?? "");
                    outCsv.WriteField(c?.SocialLinks ?? "");
                    outCsv.WriteField(c?.Addresses ?? "");
                    outCsv.WriteField(match.Score?.ToString("F3") ?? "0");
                    await outCsv.NextRecordAsync();
                }
            }
            else
            {
                outCsv.WriteField(analysisResult.Request?.InputName);
                outCsv.WriteField(analysisResult.Request?.InputWebsite);
                outCsv.WriteField(analysisResult.Request?.InputPhone);
                outCsv.WriteField(analysisResult.Request?.InputFacebook);
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("");
                outCsv.WriteField("0");
                await outCsv.NextRecordAsync();
            }
        }
    }
}