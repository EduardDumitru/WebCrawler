using System.Globalization;
using CsvHelper;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Merging
{
    public class CrawlResultMerger(ISinkFactory sinkFactory) : ICrawlResultMerger
    {
        public async Task<List<CompanyExtended>> GetMergedResults(string crawlResultsPath, string companyNamesPath, CancellationToken cancellationToken = default)
        {
            var crawlResults = new Dictionary<string, Company>(StringComparer.OrdinalIgnoreCase);
            var companyNames = new Dictionary<string, (string Commercial, string Legal, string AllNames)>(StringComparer.OrdinalIgnoreCase);

            // Read crawl results
            using (var reader = new StreamReader(crawlResultsPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                await foreach (var rec in csv.GetRecordsAsync<Company>(cancellationToken))
                {
                    crawlResults[rec.Domain] = rec;
                }
            }

            // Read company names
            using (var reader = new StreamReader(companyNamesPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                await foreach (var rec in csv.GetRecordsAsync<dynamic>(cancellationToken))
                {
                    string domain = rec.domain;
                    string commercial = rec.company_commercial_name ?? string.Empty;
                    string legal = rec.company_legal_name ?? string.Empty;
                    string allNames = rec.company_all_available_names ?? string.Empty;
                    companyNames[domain] = (commercial, legal, allNames);
                }
            }

            // Merge
            var merged = new List<CompanyExtended>();
            foreach (var kvp in crawlResults)
            {
                var domain = kvp.Key;
                var company = kvp.Value;
                companyNames.TryGetValue(domain, out var names);

                merged.Add(new CompanyExtended
                {
                    Domain = domain,
                    CompanyCommercialName = names.Commercial ?? string.Empty,
                    CompanyLegalName = names.Legal ?? string.Empty,
                    CompanyAllAvailableNames = names.AllNames ?? string.Empty,
                    Phones = company.Phones,
                    SocialLinks = company.SocialLinks,
                    Addresses = company.Addresses
                });
            }

            return merged;
        }

        public async Task WriteMergedResultsToCsv(IEnumerable<CompanyExtended> mergedResults, string outputCsvPath, CancellationToken cancellationToken = default)
        {
            using var writer = new StreamWriter(outputCsvPath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(mergedResults, cancellationToken);
        }

        public async Task WriteMergedResultsToDataSink(IEnumerable<CompanyExtended> mergedResults, CancellationToken cancellationToken = default)
        {
            await sinkFactory.GetSink(SinkType.ElasticSearch).WriteCompaniesAsync(mergedResults, ElasticSearchIndexes.Companies, cancellationToken);
        }

        public List<string> SplitExcelOrCsv(string filePath, int splitCount)
        {
            var outputDir = Path.GetDirectoryName(filePath) ?? ".";
            var allLines = File.ReadAllLines(filePath);
            if (allLines.Length < 2) throw new InvalidOperationException("No data rows found.");

            var header = allLines[0];
            var dataRows = allLines.Skip(1).ToList();
            int chunkSize = (int)Math.Ceiling(dataRows.Count / (double)splitCount);

            var splitFiles = new List<string>();
            for (int i = 0; i < splitCount; i++)
            {
                var chunk = dataRows.Skip(i * chunkSize).Take(chunkSize).ToList();
                if (chunk.Count == 0) break;

                var outFile = Path.Combine(outputDir, $"domains_{i + 1}.csv");
                File.WriteAllLines(outFile, new[] { header }.Concat(chunk));
                splitFiles.Add(outFile);
            }
            return splitFiles;
        }
    }
}