using System.Collections.Concurrent;
using System.Globalization;
using CsvHelper;
using HtmlAgilityPack;
using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;
using WebCrawler.Helpers;
using WebCrawler.Services.Extraction;

namespace WebCrawler.Services.Crawling
{
    public partial class CrawlService(IHttpClientFactory httpClientFactory, PlaywrightManager playwrightManager, ICrawlResultWriter crawlResultWriter) : ICrawlService
    {
        #region Constants and Static Fields

        // Performance tuning constants
        private const int MAX_HTTP_PARALLELISM = 50;

        private const int MAX_PLAYWRIGHT_PARALLELISM = 2;
        private const int HTTP_TIMEOUT_SECONDS = 20;
        private const int PLAYWRIGHT_TIMEOUT_SECONDS = 25;
        private const int MAX_HTML_SIZE = 1_000_000;
        private const int BATCH_SIZE = 25;

        #endregion Constants and Static Fields

        #region Public Methods

        public async Task<IList<string>> GetDomains(CancellationToken cancellationToken = default)
        {
            // Use the environment variable if set, otherwise fallback to default
            var domainFile = Environment.GetEnvironmentVariable("DOMAIN_FILE") ?? "sample-websites.csv";
            using var reader = new StreamReader(domainFile);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var list = new List<string>();

            await foreach (var r in csv.GetRecordsAsync<Website>(cancellationToken))
            {
                list.Add(r.Domain);
            }

            return list;
        }

        public async Task CrawlWebsitesAsync(string containerId, CancellationToken cancellationToken = default)
        {
            var domains = await GetDomains(cancellationToken);
            var results = new ConcurrentBag<CrawlResult>();
            var crawledDomains = new ConcurrentBag<string>();
            var failedDomains = new ConcurrentBag<(string Domain, string Reason)>();

            var httpClient = httpClientFactory.CreateClient("Default");

            // Configure HttpClient for better performance
            ConfigureHttpClientForPerformance(httpClient);

            // Process domains in batches to manage memory
            var batches = domains.Chunk(BATCH_SIZE).ToList();

            LoggerHelper.LogToFile($"[START] Processing {domains.Count} domains in {batches.Count} batches of {BATCH_SIZE}");

            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                LoggerHelper.LogToFile($"[BATCH] Processing batch {i + 1}/{batches.Count} with {batch.Length} domains");

                await ProcessBatchAsync(batch, httpClient, results, crawledDomains, failedDomains, cancellationToken);

                // Force GC between batches
                if (i % 2 == 0) // Every 2 batches
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                }
            }

            LoggerHelper.LogToFile($"[COMPLETE] Finished processing all domains. Successful: {crawledDomains.Count}, Failed: {failedDomains.Count}");
            // Only include domains for which we have results
            var successfulDomains = results.Select(r => r.Domain).Distinct().ToList();
            await crawlResultWriter.SaveResultsToCsv(successfulDomains, results, containerId, cancellationToken: cancellationToken);
        }

        #endregion Public Methods

        #region HTTP Client Configuration

        private static void ConfigureHttpClientForPerformance(HttpClient httpClient)
        {
            httpClient.Timeout = TimeSpan.FromSeconds(HTTP_TIMEOUT_SECONDS);
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        }

        #endregion HTTP Client Configuration

        #region Batch Processing

        private async Task ProcessBatchAsync(
            string[] batch,
            HttpClient httpClient,
            ConcurrentBag<CrawlResult> results,
            ConcurrentBag<string> crawledDomains,
            ConcurrentBag<(string Domain, string Reason)> failedDomains,
            CancellationToken cancellationToken)
        {
            // Step 1: Try HTTP requests first with high parallelism
            var httpSuccessful = new ConcurrentBag<(string Domain, string Html)>();
            var httpFailed = new ConcurrentBag<string>();

            await ProcessHttpRequestsAsync(batch, httpClient, httpSuccessful, httpFailed, cancellationToken);

            // Step 2: Process successful HTTP responses
            await ProcessHttpResponsesAsync([.. httpSuccessful], results, crawledDomains, failedDomains, cancellationToken);

            // Step 3: Try Playwright for failed HTTP requests (with much lower parallelism)
            if (!httpFailed.IsEmpty)
            {
                await ProcessPlaywrightFallbackAsync([.. httpFailed], results, crawledDomains, failedDomains, cancellationToken);
            }
        }

        private static async Task ProcessHttpRequestsAsync(
            string[] domains,
            HttpClient httpClient,
            ConcurrentBag<(string Domain, string Html)> successful,
            ConcurrentBag<string> failed,
            CancellationToken cancellationToken)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MAX_HTTP_PARALLELISM,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(domains, parallelOptions, async (domain, ct) =>
            {
                try
                {
                    // Quick robots.txt check (with timeout)
                    using var robotsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    robotsCts.CancelAfter(TimeSpan.FromSeconds(10));

                    if (!await RobotsChecker.IsAllowedAsync(domain, httpClient, "*", "/", robotsCts.Token))
                    {
                        failed.Add(domain);
                        return;
                    }

                    // HTTP request with timeout
                    using var httpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    httpCts.CancelAfter(TimeSpan.FromSeconds(HTTP_TIMEOUT_SECONDS));

                    var response = await httpClient.GetAsync($"https://{domain}", httpCts.Token);

                    if ((int)response.StatusCode >= 400)
                    {
                        failed.Add(domain);
                        return;
                    }

                    // Check for redirects to parking domains
                    var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                    if (!string.Equals(finalUrl, $"https://{domain}/", StringComparison.OrdinalIgnoreCase) &&
                        HtmlDataExtractor.IsParkedDomain(finalUrl))
                    {
                        failed.Add(domain);
                        return;
                    }

                    var html = await response.Content.ReadAsStringAsync(httpCts.Token);

                    // Quick content validation
                    if (HtmlDataExtractor.IsParkedContent(html) || !HtmlDataExtractor.HasValidBody(html))
                    {
                        failed.Add(domain);
                        return;
                    }

                    // Limit HTML size early
                    if (html.Length > MAX_HTML_SIZE)
                    {
                        html = html[..MAX_HTML_SIZE];
                    }

                    successful.Add((domain, html));
                }
                catch (Exception ex)
                {
                    LoggerHelper.LogToFile($"[HTTP-ERROR] {domain}: {ex.Message}");
                    failed.Add(domain);
                }
            });

            LoggerHelper.LogToFile($"[HTTP] Completed: {successful.Count} successful, {failed.Count} failed");
        }

        private static async Task ProcessHttpResponsesAsync(
            List<(string Domain, string Html)> successful,
            ConcurrentBag<CrawlResult> results,
            ConcurrentBag<string> crawledDomains,
            ConcurrentBag<(string Domain, string Reason)> failedDomains,
            CancellationToken cancellationToken)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount * 2, // CPU-bound processing
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(successful, parallelOptions, async (item, ct) =>
            {
                try
                {
                    var (domain, html) = item;
                    var crawlResult = await ExtractDataFromHtmlAsync(html, domain);

                    results.Add(crawlResult);
                    crawledDomains.Add(domain);
                }
                catch (Exception ex)
                {
                    LoggerHelper.LogToFile($"[EXTRACT-ERROR] {item.Domain}: {ex.Message}");
                    failedDomains.Add((item.Domain, ex.Message));
                }
            });
        }

        private async Task ProcessPlaywrightFallbackAsync(
            List<string> failedDomains,
            ConcurrentBag<CrawlResult> results,
            ConcurrentBag<string> crawledDomains,
            ConcurrentBag<(string Domain, string Reason)> finalFailedDomains,
            CancellationToken cancellationToken)
        {
            LoggerHelper.LogToFile($"[PLAYWRIGHT] Starting fallback processing for {failedDomains.Count} domains");

            // Much lower parallelism for Playwright
            using var semaphore = new SemaphoreSlim(MAX_PLAYWRIGHT_PARALLELISM);

            var tasks = failedDomains.Select(async domain =>
            {
                try
                {
                    await semaphore.WaitAsync(cancellationToken);

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(PLAYWRIGHT_TIMEOUT_SECONDS));

                    var html = await playwrightManager.GetPageContentWithTimeoutHandlingAsync(domain, timeoutCts.Token);

                    if (HtmlDataExtractor.IsParkedContent(html))
                    {
                        finalFailedDomains.Add((domain, "Domain is parked"));
                        return;
                    }

                    if (html.Length > MAX_HTML_SIZE)
                    {
                        html = html[..MAX_HTML_SIZE];
                    }

                    var crawlResult = await ExtractDataFromHtmlAsync(html, domain);

                    results.Add(crawlResult);
                    crawledDomains.Add(domain);
                }
                catch (Exception ex)
                {
                    LoggerHelper.LogToFile($"[PLAYWRIGHT-ERROR] {domain}: {ex.Message}");
                    finalFailedDomains.Add((domain, ex.Message));
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            LoggerHelper.LogToFile($"[PLAYWRIGHT] Completed fallback processing");
        }

        #endregion Batch Processing

        #region Data Extraction

        private static async Task<CrawlResult> ExtractDataFromHtmlAsync(string html, string domain)
        {
            return await Task.Run(() =>
            {
                var phones = new List<string>();
                var socialLinks = new List<string>();
                var addresses = new List<string>();

                try
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                    if (bodyNode == null)
                    {
                        return new CrawlResult
                        {
                            Phones = phones.Distinct().ToList(),
                            SocialLinks = socialLinks.Distinct().ToList(),
                            Addresses = addresses.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList(),
                            Domain = domain,
                        }; ;
                    }

                    HtmlDataExtractor.CleanHtmlBody(bodyNode);
                    var cleanedHtml = bodyNode.OuterHtml;

                    // Extract data
                    var extractedPhones = HtmlDataExtractor.ExtractPhones(bodyNode, cleanedHtml);
                    if (extractedPhones is not null && extractedPhones.Any(ep => ep != null))
                    {
                        phones.AddRange(extractedPhones!);
                    }

                    var (extractedSocialLinks, mapAddresses) = HtmlDataExtractor.ExtractLinksFromBody(bodyNode);
                    socialLinks.AddRange(extractedSocialLinks);
                    addresses.AddRange(mapAddresses);

                    addresses.AddRange(HtmlDataExtractor.ExtractAddressesFromBody(bodyNode, phones,
                        HtmlDataExtractor.ExtractEmailsFromAnchors(bodyNode)));

                    // Remove duplicates efficiently
                    return new CrawlResult
                    {
                        Phones = phones.Distinct().ToList(),
                        SocialLinks = socialLinks.Distinct().ToList(),
                        Addresses = addresses.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList(),
                        Domain = domain,
                    };
                }
                catch (Exception ex)
                {
                    LoggerHelper.LogToFile($"[EXTRACT-ERROR] {domain}: {ex.Message}");
                    return new CrawlResult
                    {
                        Phones = phones.Distinct().ToList(),
                        SocialLinks = socialLinks.Distinct().ToList(),
                        Addresses = addresses.Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList(),
                        Domain = domain,
                    };
                }
            });
        }

        #endregion Data Extraction
    }
}