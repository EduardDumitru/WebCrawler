using System.Text;
using Moq;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Services.Merging;
using Xunit;

namespace WebCrawler.Tests
{
    public class CrawlResultMergerTests
    {
        [Fact]
        public async Task GetMergedResults_MergesCompanyDataCorrectly()
        {
            // Arrange
            var sinkMock = new Mock<ISink>();
            var merger = new CrawlResultMerger(sinkMock.Object);
            var tempDir = Path.Combine(Path.GetTempPath(), "merger-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var crawlResultsPath = Path.Combine(tempDir, "crawl-results.csv");
            var companyNamesPath = Path.Combine(tempDir, "company-names.csv");

            // Write sample crawl results
            var crawlCsv = new StringBuilder();
            crawlCsv.AppendLine("Domain,Phones,SocialLinks,Addresses");
            crawlCsv.AppendLine("example.com,12345,fb.com/example,123 Main St");
            File.WriteAllText(crawlResultsPath, crawlCsv.ToString());

            // Write sample company names
            var namesCsv = new StringBuilder();
            namesCsv.AppendLine("domain,company_commercial_name,company_legal_name,company_all_available_names");
            namesCsv.AppendLine("example.com,Example Commercial,Example Legal,Example All");
            File.WriteAllText(companyNamesPath, namesCsv.ToString());

            // Act
            var results = await merger.GetMergedResults(crawlResultsPath, companyNamesPath);

            // Assert
            Assert.Single(results);
            var merged = results[0];
            Assert.Equal("example.com", merged.Domain);
            Assert.Equal("Example Commercial", merged.CompanyCommercialName);
            Assert.Equal("Example Legal", merged.CompanyLegalName);
            Assert.Equal("Example All", merged.CompanyAllAvailableNames);
            Assert.Equal("12345", merged.Phones);
            Assert.Equal("fb.com/example", merged.SocialLinks);
            Assert.Equal("123 Main St", merged.Addresses);

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task GetMergedResults_EmptyIfNoDataRows()
        {
            // Arrange
            var sinkMock = new Mock<ISink>();
            var merger = new CrawlResultMerger(sinkMock.Object);
            var tempDir = Path.Combine(Path.GetTempPath(), "merger-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var crawlResultsPath = Path.Combine(tempDir, "crawl-results.csv");
            var companyNamesPath = Path.Combine(tempDir, "company-names.csv");

            // Write only headers (no data)
            File.WriteAllText(crawlResultsPath, "Domain,Phones,SocialLinks,Addresses\n");
            File.WriteAllText(companyNamesPath, "domain,company_commercial_name,company_legal_name,company_all_available_names\n");

            // Act
            var results = await merger.GetMergedResults(crawlResultsPath, companyNamesPath);

            // Assert
            Assert.Empty(results);

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public void SplitExcelOrCsv_SplitsFileCorrectly()
        {
            // Arrange
            var sinkMock = new Mock<ISink>();
            var merger = new CrawlResultMerger(sinkMock.Object);
            var tempDir = Path.Combine(Path.GetTempPath(), "merger-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var filePath = Path.Combine(tempDir, "domains.csv");
            var csv = new StringBuilder();
            csv.AppendLine("Domain");
            for (int i = 1; i <= 10; i++)
                csv.AppendLine($"site{i}.com");
            File.WriteAllText(filePath, csv.ToString());

            // Act
            var splitFiles = merger.SplitExcelOrCsv(filePath, 3);

            // Assert
            Assert.Equal(3, splitFiles.Count);
            foreach (var splitFile in splitFiles)
            {
                Assert.True(File.Exists(splitFile));
            }

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task WriteMergedResultsToDataSink_CallsSinkWriteAsync()
        {
            // Arrange
            var sinkMock = new Mock<ISink>();
            var merger = new CrawlResultMerger(sinkMock.Object);
            var mergedResults = new List<CompanyExtended>
            {
                new CompanyExtended
                {
                    Domain = "example.com",
                    CompanyCommercialName = "Example",
                    CompanyLegalName = "Example LLC",
                    CompanyAllAvailableNames = "Example",
                    Phones = "12345",
                    SocialLinks = "fb.com/example",
                    Addresses = "123 Main St"
                }
            };

            // Act
            await merger.WriteMergedResultsToDataSink(mergedResults, It.IsAny<CancellationToken>());

            // Assert
            sinkMock.Verify(
                s => s.WriteCompaniesAsync(
                    It.Is<IEnumerable<CompanyExtended>>(r => r.SequenceEqual(mergedResults)),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task WriteMergedResultsToCsv_WritesFileCorrectly()
        {
            // Arrange
            var sinkMock = new Mock<ISink>();
            var merger = new CrawlResultMerger(sinkMock.Object);
            var tempDir = Path.Combine(Path.GetTempPath(), "merger-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var outputCsvPath = Path.Combine(tempDir, "output.csv");
            var mergedResults = new List<CompanyExtended>
            {
                new CompanyExtended
                {
                    Domain = "example.com",
                    CompanyCommercialName = "Example",
                    CompanyLegalName = "Example LLC",
                    CompanyAllAvailableNames = "Example",
                    Phones = "12345",
                    SocialLinks = "fb.com/example",
                    Addresses = "123 Main St"
                }
            };

            // Act
            await merger.WriteMergedResultsToCsv(mergedResults, outputCsvPath);

            // Assert
            Assert.True(File.Exists(outputCsvPath));
            var content = File.ReadAllText(outputCsvPath);
            Assert.Contains("example.com", content);
            Assert.Contains("Example LLC", content);

            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }
}
