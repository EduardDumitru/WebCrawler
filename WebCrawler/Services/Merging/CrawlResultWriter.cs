using System.Collections.Concurrent;
using System.Globalization;
using CsvHelper;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Helpers;

namespace WebCrawler.Services.Merging
{
    public class CrawlResultWriter : ICrawlResultWriter
    {
        public async Task SaveResultsToCsv(
            IList<string> domains,
            ConcurrentBag<CrawlResult> results,
            string containerId,
            string resultsDir = "results",
            CancellationToken cancellationToken = default
                                          )
        {
            // Ensure the results directory exists
            if (!Directory.Exists(resultsDir))
                Directory.CreateDirectory(resultsDir);

            LoggerHelper.LogToFile("Results folder created or exists already. Saving results to CSV...");

            await SaveMainCsvResults(results, containerId, cancellationToken);
            await UpsertCrawlCoverageCSV(domains, [.. results], containerId);
        }

        public async Task CombineDomainCSVs(string resultsDir = "results", CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(resultsDir, "crawl-results-container-*.csv");
            var allCompanies = new Dictionary<string, Company>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                using var reader = new StreamReader(file);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                await foreach (var rec in csv.GetRecordsAsync<Company>(cancellationToken))
                {
                    allCompanies[rec.Domain] = rec; // Last write wins if duplicate
                }
            }

            await WriteResultsToCsv(Path.Combine(resultsDir, "crawl-results.csv"), [.. allCompanies.Values], cancellationToken);
        }

        public async Task CombineCoverageCSVs(string resultsDir = "results", CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(resultsDir, "crawl-coverage-container-*.csv");
            int totalWebsites = 0;
            int successfullyCrawled = 0;

            foreach (var file in files)
            {
                using var reader = new StreamReader(file);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                await csv.ReadAsync();
                csv.ReadHeader();
                if (await csv.ReadAsync())
                {
                    totalWebsites += csv.GetField<int>("TotalWebsites");
                    successfullyCrawled += csv.GetField<int>("SuccessfullyCrawled");
                }
            }

            using var writer = new StreamWriter(Path.Combine(resultsDir, "crawl-coverage.csv"));
            using var outCsv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            outCsv.WriteField("TotalWebsites");
            outCsv.WriteField("SuccessfullyCrawled");
            await outCsv.NextRecordAsync();
            outCsv.WriteField(totalWebsites);
            outCsv.WriteField(successfullyCrawled);
            await outCsv.NextRecordAsync();
        }

        public async Task CombineFillRatesCSVs(string resultsDir = "results", CancellationToken cancellationToken = default)
        {
            var files = Directory.GetFiles(resultsDir, "crawl-fillrates-container-*.csv");
            int websitesWithPhones = 0;
            int totalPhones = 0;
            int websitesWithSocialLinks = 0;
            int totalSocialLinks = 0;
            int websitesWithAddresses = 0;
            int totalAddresses = 0;

            foreach (var file in files)
            {
                using var reader = new StreamReader(file);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                await csv.ReadAsync();
                csv.ReadHeader();
                if (await csv.ReadAsync())
                {
                    websitesWithPhones += csv.GetField<int>("WebsitesWithPhones");
                    totalPhones += csv.GetField<int>("TotalPhones");
                    websitesWithSocialLinks += csv.GetField<int>("WebsitesWithSocialLinks");
                    totalSocialLinks += csv.GetField<int>("TotalSocialLinks");
                    websitesWithAddresses += csv.GetField<int>("WebsitesWithAddresses");
                    totalAddresses += csv.GetField<int>("TotalAddresses");
                }
            }

            using var writer = new StreamWriter(Path.Combine(resultsDir, "crawl-fillrates.csv"));
            using var outCsv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            outCsv.WriteField("WebsitesWithPhones");
            outCsv.WriteField("TotalPhones");
            outCsv.WriteField("WebsitesWithSocialLinks");
            outCsv.WriteField("TotalSocialLinks");
            outCsv.WriteField("WebsitesWithAddresses");
            outCsv.WriteField("TotalAddresses");
            await outCsv.NextRecordAsync();
            outCsv.WriteField(websitesWithPhones);
            outCsv.WriteField(totalPhones);
            outCsv.WriteField(websitesWithSocialLinks);
            outCsv.WriteField(totalSocialLinks);
            outCsv.WriteField(websitesWithAddresses);
            outCsv.WriteField(totalAddresses);
            await outCsv.NextRecordAsync();
        }

        private static async Task SaveMainCsvResults(ConcurrentBag<CrawlResult> results, string containerId, CancellationToken cancellationToken = default)
        {
            var outputPath = $"results/crawl-results-container-{containerId}.csv";
            // No need to read/merge existing results, just write this container's results
            var companies = results.Select(r => new Company
            {
                Domain = r.Domain,
                Phones = string.Join(" | ", r.Phones),
                SocialLinks = string.Join(" | ", r.SocialLinks),
                Addresses = string.Join(" | ", r.Addresses)
            }).ToList();

            await WriteResultsToCsv(outputPath, companies, cancellationToken);
        }

        private static async Task WriteResultsToCsv(string outputPath, List<Company> results, CancellationToken cancellationToken = default)
        {
            using var writer = new StreamWriter(outputPath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteHeader<Company>();
            await csv.NextRecordAsync();

            await csv.WriteRecordsAsync(results, cancellationToken);
        }

        private static async Task UpsertCrawlCoverageCSV(IList<string> domains, IList<CrawlResult> results, string containerId)
        {
            int totalWebsites = domains.Count;
            int successfullyCrawled = results.Count;

            await WriteCoverageCsv(totalWebsites, successfullyCrawled, containerId);
            await WriteFillRatesCsv(results, containerId);
        }

        private static async Task WriteCoverageCsv(int totalWebsites, int successfullyCrawled, string containerId)
        {
            using var writer = new StreamWriter($"results/crawl-coverage-container-{containerId}.csv");
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteField("TotalWebsites");
            csv.WriteField("SuccessfullyCrawled");
            await csv.NextRecordAsync();

            csv.WriteField(totalWebsites);
            csv.WriteField(successfullyCrawled);
            await csv.NextRecordAsync();
        }

        private static async Task WriteFillRatesCsv(IList<CrawlResult> results, string containerId)
        {
            int websitesWithPhones = results.Count(r => r.Phones.Count > 0);
            int totalPhones = results.Sum(r => r.Phones.Count);
            int websitesWithSocialLinks = results.Count(r => r.SocialLinks.Count > 0);
            int totalSocialLinks = results.Sum(r => r.SocialLinks.Count);
            int websitesWithAddresses = results.Count(r => r.Addresses.Count > 0);
            int totalAddresses = results.Sum(r => r.Addresses.Count);

            using var writer = new StreamWriter($"results/crawl-fillrates-container-{containerId}.csv");
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteField("WebsitesWithPhones");
            csv.WriteField("TotalPhones");
            csv.WriteField("WebsitesWithSocialLinks");
            csv.WriteField("TotalSocialLinks");
            csv.WriteField("WebsitesWithAddresses");
            csv.WriteField("TotalAddresses");
            await csv.NextRecordAsync();

            csv.WriteField(websitesWithPhones);
            csv.WriteField(totalPhones);
            csv.WriteField(websitesWithSocialLinks);
            csv.WriteField(totalSocialLinks);
            csv.WriteField(websitesWithAddresses);
            csv.WriteField(totalAddresses);
            await csv.NextRecordAsync();
        }
    }
}