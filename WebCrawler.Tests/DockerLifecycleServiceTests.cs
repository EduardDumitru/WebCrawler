using Moq;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Services.Docker;

namespace WebCrawler.Tests
{
    public class DockerLifecycleServiceTests
    {
        [Fact]
        public async Task RunCrawlAsync_CallsCrawlServiceAndCreatesDoneFile()
        {
            // Arrange
            var crawlServiceMock = new Mock<ICrawlService>();
            var crawlResultWriterMock = new Mock<ICrawlResultWriter>();
            var crawlResultMergerMock = new Mock<ICrawlResultMerger>();
            var service = new DockerLifecycleService(
                crawlServiceMock.Object,
                crawlResultWriterMock.Object,
                crawlResultMergerMock.Object);

            // Use a unique temp directory for isolation
            var tempResultsDir = Path.Combine(Path.GetTempPath(), "results-" + Guid.NewGuid());
            Directory.CreateDirectory(tempResultsDir);

            var containerId = "test-container";
            var crawlerIndex = "1";
            var doneFile = $"test-done-{Guid.NewGuid()}.done";
            var doneFilePath = Path.Combine(tempResultsDir, doneFile);

            // Act
            await service.RunCrawlAsync(containerId, crawlerIndex, doneFile, tempResultsDir);

            // Assert
            crawlServiceMock.Verify(x => x.CrawlWebsitesAsync(containerId, It.IsAny<CancellationToken>()), Times.Once());
            Assert.True(File.Exists(doneFilePath));

            // Cleanup
            File.Delete(doneFilePath);
            Directory.Delete(tempResultsDir, true);
        }

        [Fact]
        public async Task RunCombineAndMergeAsync_CallsAllSteps()
        {
            // Arrange
            var crawlServiceMock = new Mock<ICrawlService>();
            var crawlResultWriterMock = new Mock<ICrawlResultWriter>();
            var crawlResultMergerMock = new Mock<ICrawlResultMerger>();

            crawlResultWriterMock.Setup(x => x.CombineDomainCSVs("results", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            crawlResultWriterMock.Setup(x => x.CombineCoverageCSVs("results", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            crawlResultWriterMock.Setup(x => x.CombineFillRatesCSVs("results", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            crawlResultMergerMock.Setup(x => x.GetMergedResults(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CompanyExtended>());

            crawlResultMergerMock.Setup(x => x.WriteMergedResultsToCsv(
                It.IsAny<IEnumerable<CompanyExtended>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            crawlResultMergerMock.Setup(x => x.WriteMergedResultsToDataSink(
                It.IsAny<IEnumerable<CompanyExtended>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new DockerLifecycleService(
                crawlServiceMock.Object,
                crawlResultWriterMock.Object,
                crawlResultMergerMock.Object);

            // Ensure the "results" directory exists in the current working directory
            var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
            if (!Directory.Exists(resultsDir))
                Directory.CreateDirectory(resultsDir);

            // Act
            await service.RunCombineAndMergeAsync();

            // Assert
            crawlResultWriterMock.Verify(x => x.CombineDomainCSVs("results", It.IsAny<CancellationToken>()), Times.Once());
            crawlResultWriterMock.Verify(x => x.CombineCoverageCSVs("results", It.IsAny<CancellationToken>()), Times.Once());
            crawlResultWriterMock.Verify(x => x.CombineFillRatesCSVs("results", It.IsAny<CancellationToken>()), Times.Once());
            crawlResultMergerMock.Verify(x => x.GetMergedResults(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
            crawlResultMergerMock.Verify(x => x.WriteMergedResultsToCsv(
                It.IsAny<IEnumerable<CompanyExtended>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
            crawlResultMergerMock.Verify(x => x.WriteMergedResultsToDataSink(
                It.IsAny<IEnumerable<CompanyExtended>>(), It.IsAny<CancellationToken>()), Times.Once());

            // Cleanup
            Directory.Delete(resultsDir, true);
        }

    }
}
