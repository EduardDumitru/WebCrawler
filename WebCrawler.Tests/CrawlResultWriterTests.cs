using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Moq;
using WebCrawler.Core.Models;
using WebCrawler.Services.Merging;
using Xunit;

namespace WebCrawler.Tests
{
    public class CrawlResultWriterTests
    {
        [Fact]
        public async Task SaveResultsToCsv_CreatesCsvFiles()
        {
            // Arrange
            var writer = new CrawlResultWriter();
            var tempDir = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            var domains = new List<string> { "example.com", "test.com" };
            var results = new ConcurrentBag<CrawlResult>
            {
                new CrawlResult { Domain = "example.com", Phones = new List<string> { "12345" }, SocialLinks = new List<string> { "fb.com/example" }, Addresses = new List<string> { "123 Main St" } },
                new CrawlResult { Domain = "test.com", Phones = new List < string > { "67890" }, SocialLinks = new List < string > { "fb.com/test" }, Addresses = new List < string > { "456 Test Ave" } }
            };
            var containerId = Guid.NewGuid().ToString();

            // Act
            await writer.SaveResultsToCsv(domains, results, containerId, tempDir, It.IsAny<CancellationToken>());

            // Assert
            var mainCsv = Path.Combine(tempDir, $"crawl-results-{containerId}.csv");
            Assert.True(File.Exists(mainCsv));
            var content = File.ReadAllText(mainCsv);
            Assert.Contains("example.com", content);
            Assert.Contains("test.com", content);

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task CombineDomainCSVs_CombinesFiles()
        {
            // Arrange
            var writer = new CrawlResultWriter();
            var tempDir = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            // Create two partial result files
            var csv1 = new StringBuilder();
            csv1.AppendLine("Domain,Phones,SocialLinks,Addresses");
            csv1.AppendLine("example.com,12345,fb.com/example,123 Main St");
            File.WriteAllText(Path.Combine(tempDir, "crawl-results-container-1.csv"), csv1.ToString());

            var csv2 = new StringBuilder();
            csv2.AppendLine("Domain,Phones,SocialLinks,Addresses");
            csv2.AppendLine("test.com,67890,fb.com/test,456 Test Ave");
            File.WriteAllText(Path.Combine(tempDir, "crawl-results-container-2.csv"), csv2.ToString());

            // Act
            await writer.CombineDomainCSVs(tempDir, It.IsAny<CancellationToken>());

            // Assert
            var combined = Path.Combine(tempDir, "crawl-results.csv");
            Assert.True(File.Exists(combined));
            var content = File.ReadAllText(combined);
            Assert.Contains("example.com", content);
            Assert.Contains("test.com", content);

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task CombineCoverageCSVs_CombinesFiles()
        {
            // Arrange
            var writer = new CrawlResultWriter();
            var tempDir = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            // Create two coverage files
            File.WriteAllText(Path.Combine(tempDir, "crawl-coverage-container-1.csv"), "TotalWebsites,SuccessfullyCrawled\n2,1\n");
            File.WriteAllText(Path.Combine(tempDir, "crawl-coverage-container-2.csv"), "TotalWebsites,SuccessfullyCrawled\n3,2\n");

            // Act
            await writer.CombineCoverageCSVs(tempDir, It.IsAny<CancellationToken>());

            // Assert
            var combined = Path.Combine(tempDir, "crawl-coverage.csv");
            Assert.True(File.Exists(combined));
            var content = File.ReadAllText(combined);
            Assert.Contains("5", content); // TotalWebsites
            Assert.Contains("3", content); // SuccessfullyCrawled

            // Cleanup
            Directory.Delete(tempDir, true);
        }

        [Fact]
        public async Task CombineFillRatesCSVs_CombinesFiles()
        {
            // Arrange
            var writer = new CrawlResultWriter();
            var tempDir = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            // Create two fillrate files
            File.WriteAllText(Path.Combine(tempDir, "crawl-fillrates-container-1.csv"), "WebsitesWithPhones,TotalPhones,WebsitesWithSocialLinks,TotalSocialLinks,WebsitesWithAddresses,TotalAddresses\n1,2,1,2,1,2\n");
            File.WriteAllText(Path.Combine(tempDir, "crawl-fillrates-container-2.csv"), "WebsitesWithPhones,TotalPhones,WebsitesWithSocialLinks,TotalSocialLinks,WebsitesWithAddresses,TotalAddresses\n2,3,2,3,2,3\n");

            // Act
            await writer.CombineFillRatesCSVs(tempDir, It.IsAny<CancellationToken>());

            // Assert
            var combined = Path.Combine(tempDir, "crawl-fillrates.csv");
            Assert.True(File.Exists(combined));
            var content = File.ReadAllText(combined);
            Assert.Contains("3", content); // WebsitesWithPhones
            Assert.Contains("5", content); // TotalPhones

            // Cleanup
            Directory.Delete(tempDir, true);
        }
    }
}
