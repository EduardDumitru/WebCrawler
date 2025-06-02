using System.Diagnostics;
using Microsoft.Playwright;
using WebCrawler.Helpers;

namespace WebCrawler.Services.Crawling
{
    public class PlaywrightManager : IDisposable, IAsyncDisposable
    {
        private static readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private static readonly SemaphoreSlim _pageSemaphore = new(3, 3);

        private DateTime _browserCreationTime = DateTime.Now;
        private int _pageCounter = 0;
        private const int MAX_PAGES_PER_BROWSER = 150; // Recreate browser frequently
        private const int MAX_BROWSER_AGE_MINUTES = 10; // Very short browser lifetime

        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private bool _disposed = false;
        private readonly Lock _disposeLock = new();

        // Minimal browser options - remove anything that could cause hangs
        private static readonly string[] options =
        [
            "--no-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--disable-extensions",
        "--disable-plugins",
        "--disable-web-security",
        "--disable-features=VizDisplayCompositor",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
        "--disable-hang-monitor",
        "--disable-sync",
        "--no-first-run",
        "--aggressive-cache-discard",
        "--max-old-space-size=512" // Limit memory
        ];

        private static readonly BrowserNewContextOptions browserOptions = new()
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
        };

        private readonly Lock _browserLock = new();

        private IBrowser? GetBrowserSafely()
        {
            lock (_browserLock)
            {
                return _browser?.IsConnected == true ? _browser : null;
            }
        }

        public async Task InitializeAsync(bool forceRecreate = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PlaywrightManager));

            // Very short timeout - don't wait long for initialization
            using var initTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (!await _initSemaphore.WaitAsync(TimeSpan.FromSeconds(5), initTimeout.Token))
                throw new TimeoutException("Browser initialization timed out");

            try
            {
                bool needRecreation = forceRecreate ||
                                      _browser == null ||
                                      _browser?.IsConnected != true ||
                                      _pageCounter >= MAX_PAGES_PER_BROWSER ||
                                      (DateTime.Now - _browserCreationTime).TotalMinutes > MAX_BROWSER_AGE_MINUTES;

                if (needRecreation)
                {
                    await KillBrowserHard();
                    await CreateNewBrowserAsync(initTimeout.Token);
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        private async Task CreateNewBrowserAsync(CancellationToken cancellationToken)
        {
            try
            {
                await KillChromeProcesses();
                LoggerHelper.LogToFile("[PLAYWRIGHT] Creating Playwright instance...");
                _playwright = await Playwright.CreateAsync();
                LoggerHelper.LogToFile("[PLAYWRIGHT] Playwright instance created.");

                using var launchTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                launchTimeout.CancelAfter(TimeSpan.FromSeconds(15));

                LoggerHelper.LogToFile("[PLAYWRIGHT] Launching Chromium...");
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Timeout = 10000,
                    Args = options
                }).WaitAsync(launchTimeout.Token);
                LoggerHelper.LogToFile("[PLAYWRIGHT] Chromium launched.");

                _browserCreationTime = DateTime.Now;
                _pageCounter = 0;

                if (_browser == null || !_browser.IsConnected)
                    throw new InvalidOperationException("Browser failed to launch properly");
            }
            catch (Exception ex)
            {
                await KillBrowserHard();
                LoggerHelper.LogToFile($"[PLAYWRIGHT-ERROR] {ex}");
                throw new InvalidOperationException($"Browser creation failed: {ex.Message}", ex);
            }
        }

        private async Task KillBrowserHard()
        {
            IBrowser? browserToClose;
            IPlaywright? playwrightToDispose;

            try
            {
                // STEP 1: Atomically capture and nullify references
                lock (_browserLock)
                {
                    browserToClose = _browser;
                    playwrightToDispose = _playwright;
                    _browser = null;
                    _playwright = null;
                }

                // STEP 2: Now safely dispose the captured references outside the lock
                if (browserToClose != null)
                {
                    try
                    {
                        // Don't wait for graceful close - kill it
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (browserToClose != null)
                                {
                                    await browserToClose.CloseAsync();
                                }
                            }
                            catch { /* Ignore */ }
                        });
                    }
                    catch { /* Ignore */ }
                }

                if (playwrightToDispose != null)
                {
                    try
                    {
                        playwrightToDispose.Dispose();
                    }
                    catch { /* Ignore */ }
                }

                // STEP 3: Kill any remaining Chrome processes (your original logic)
                await KillChromeProcesses();
            }
            catch
            {
                // Ignore all disposal errors (your original approach)
            }
        }

        private static async Task KillChromeProcesses()
        {
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var chromeProcesses = Process.GetProcessesByName("chrome");
                        var chromiumProcesses = Process.GetProcessesByName("chromium");

                        foreach (var process in chromeProcesses.Concat(chromiumProcesses))
                        {
                            try
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(true); // Force kill
                                    process.WaitForExit(1000); // Wait max 1 second
                                }
                            }
                            catch { /* Ignore */ }
                            finally
                            {
                                process.Dispose();
                            }
                        }
                    }
                    catch { /* Ignore */ }
                });
            }
            catch { /* Ignore */ }
        }

        public async Task<string> GetPageContentWithTimeoutHandlingAsync(string domain, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PlaywrightManager));

            // Overall operation timeout - NEVER exceed this
            using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationTimeout.CancelAfter(TimeSpan.FromSeconds(15)); // Maximum 15 seconds per website

            var semaphoreToken = operationTimeout.Token;
            if (!await _pageSemaphore.WaitAsync(TimeSpan.FromSeconds(2), semaphoreToken))
            {
                LoggerHelper.LogToFile($"[TIMEOUT] Semaphore wait timed out for {domain}");
                return string.Empty;
            }

            try
            {
                return await ProcessSingleDomain(domain, operationTimeout.Token);
            }
            finally
            {
                _pageSemaphore.Release();
            }
        }

        private async Task<string> ProcessSingleDomain(string domain, CancellationToken cancellationToken = default)
        {
            IBrowserContext? context = null;
            IPage? page = null;

            try
            {
                await InitializeAsync();

                var browser = GetBrowserSafely();
                if (browser == null || !browser.IsConnected)
                {
                    LoggerHelper.LogToFile($"[ERROR] Browser not available for {domain}");
                    return string.Empty;
                }

                Interlocked.Increment(ref _pageCounter);
                LoggerHelper.LogToFile($"[INFO] Processing {domain} (#{_pageCounter})");

                // Create context with timeout
                try
                {
                    context = await browser.NewContextAsync(browserOptions).WaitAsync(cancellationToken);
                }
                catch (NullReferenceException)
                {
                    LoggerHelper.LogToFile($"[ERROR] Browser became null during context creation for {domain}");
                    return string.Empty;
                }
                page = await context.NewPageAsync().WaitAsync(cancellationToken);

                // Set aggressive timeouts
                page.SetDefaultTimeout(4000);
                page.SetDefaultNavigationTimeout(4000);

                // Sanitize domain
                string sanitizedDomain = domain.Trim().ToLowerInvariant();
                if (sanitizedDomain.StartsWith("http://") || sanitizedDomain.StartsWith("https://"))
                {
                    sanitizedDomain = new Uri(sanitizedDomain).Host;
                }

                return await TryNavigateQuick(page, sanitizedDomain, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LoggerHelper.LogToFile($"[TIMEOUT] Operation cancelled for {domain}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogToFile($"[ERROR] {domain}: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                // Fast cleanup - don't wait
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (page != null && !page.IsClosed)
                            await page.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch { /* Ignore */ }

                    try
                    {
                        if (context != null)
                            await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch { /* Ignore */ }
                }, cancellationToken);
            }
        }

        private static async Task<string> TryNavigateQuick(IPage page, string sanitizedDomain, CancellationToken cancellationToken)
        {
            var urlsToTry = new[]
            {
                $"http://{sanitizedDomain}",
                $"http://www.{sanitizedDomain}",
                $"https://{sanitizedDomain}",
                $"https://www.{sanitizedDomain}",
            };

            // Only try two quick strategies
            var strategies = new[]
            {
                new { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 4000 },
                new { WaitUntil = WaitUntilState.Commit, Timeout = 3000 }
            };

            foreach (string url in urlsToTry)
            {
                if (cancellationToken.IsCancellationRequested) break;

                foreach (var strategy in strategies)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        LoggerHelper.LogToFile($"[INFO] Trying {url} ({strategy.Timeout}ms)");

                        // Double timeout protection - both navigation timeout AND cancellation token
                        using var navigationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        navigationTimeout.CancelAfter(TimeSpan.FromMilliseconds(strategy.Timeout + 500)); // Extra 500ms buffer

                        var response = await page.GotoAsync(url, new PageGotoOptions
                        {
                            Timeout = strategy.Timeout,
                            WaitUntil = strategy.WaitUntil
                        }).WaitAsync(navigationTimeout.Token);

                        if (response?.Ok == true)
                        {
                            LoggerHelper.LogToFile($"[SUCCESS] Navigation OK for {url}");

                            // Content retrieval with its own timeout
                            using var contentTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            contentTimeout.CancelAfter(TimeSpan.FromSeconds(5)); // Max 5 seconds for content

                            var html = await page.ContentAsync().WaitAsync(contentTimeout.Token);

                            if (!string.IsNullOrWhiteSpace(html) && html.Length > 50)
                            {
                                LoggerHelper.LogToFile($"[SUCCESS] Got {html.Length} chars from {url}");
                                return html;
                            }
                            else
                            {
                                LoggerHelper.LogToFile($"[WARNING] Content too short ({html?.Length ?? 0} chars) from {url}");
                            }
                        }
                        else
                        {
                            LoggerHelper.LogToFile($"[WARNING] Navigation failed for {url} - Status: {response?.Status ?? 0}");
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        LoggerHelper.LogToFile($"[TIMEOUT] Overall operation cancelled for {sanitizedDomain}");
                        throw; // Propagate main cancellation
                    }
                    catch (OperationCanceledException)
                    {
                        LoggerHelper.LogToFile($"[TIMEOUT] Navigation/content timeout for {url} - moving to next");
                        continue; // Try next strategy/URL
                    }
                    catch (TimeoutException)
                    {
                        LoggerHelper.LogToFile($"[TIMEOUT] Playwright timeout for {url} - moving to next");
                        continue; // Try next strategy/URL
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogToFile($"[WARNING] {url}: {ex.GetType().Name} - {ex.Message}");
                        continue; // Try next strategy/URL
                    }
                }
            }

            LoggerHelper.LogToFile($"[FAILED] No content retrieved for {sanitizedDomain} after trying all strategies");
            return string.Empty;
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            // Force kill everything
            _ = Task.Run(async () =>
            {
                try
                {
                    await KillBrowserHard();
                }
                catch { /* Ignore */ }
            });

            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await KillBrowserHard().WaitAsync(cts.Token);
            }
            catch
            {
                // Force kill if disposal hangs
                _ = Task.Run(KillChromeProcesses);
            }

            GC.SuppressFinalize(this);
        }
    }
}