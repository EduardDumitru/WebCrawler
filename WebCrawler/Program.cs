using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Services.Crawling;
using WebCrawler.Services.Docker;
using WebCrawler.Services.Merging;
using WebCrawler.Services.Search;

var builder = WebApplication.CreateBuilder(args);

var objDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "obj");
Directory.CreateDirectory(objDir);

// Add services to the container.
builder.Services.AddHttpClient("Default", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20); // or your preferred timeout
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
    client.DefaultRequestHeaders.Add("Connection", "keep-alive");
    client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
    client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
    client.DefaultRequestHeaders.Add("Pragma", "no-cache");
    client.DefaultRequestHeaders.Add("DNT", "1"); // Do Not Track
    client.DefaultRequestHeaders.Add("TE", "Trailers");
    client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
});
//.ConfigurePrimaryHttpMessageHandler(() =>
//    new HttpClientHandler
//    {
//        Proxy = new WebProxy("http://63.141.128.80:80"),
//        UseProxy = true
//    });
builder.Services.AddSingleton<ElasticSearchSink>();
builder.Services.AddScoped<ISinkFactory, SinkFactory>();

builder.Services.AddSingleton<PlaywrightManager>();
builder.Services.AddSingleton<CompanyDataCache>();
builder.Services.AddScoped<ICrawlService, CrawlService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<ISearchBatchAnalysisService, SearchBatchAnalysisService>();
builder.Services.AddScoped<ICrawlResultMerger, CrawlResultMerger>();
builder.Services.AddScoped<ICrawlResultWriter, CrawlResultWriter>();
builder.Services.AddScoped<IDockerLifecycleService, DockerLifecycleService>();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (args.Contains("--crawl"))
{
    using var scope = app.Services.CreateScope();
    var dockerService = scope.ServiceProvider.GetRequiredService<IDockerLifecycleService>();
    var containerId = Environment.GetEnvironmentVariable("HOSTNAME") ?? System.Net.Dns.GetHostName();
    var crawlerIndex = Environment.GetEnvironmentVariable("CRAWLER_INDEX") ?? string.Empty;
    var crawlerDoneFile = Environment.GetEnvironmentVariable("CRAWLER_DONE_FILE") ?? string.Empty;
    var resultsDirectory = Environment.GetEnvironmentVariable("RESULTS_DIRECTORY")
                           ?? Path.Combine(Directory.GetCurrentDirectory(), "results");
    await dockerService.RunCrawlAsync(containerId, crawlerIndex, crawlerDoneFile, resultsDirectory);
    return;
}

if (args.Contains("--combine-and-merge"))
{
    using var scope = app.Services.CreateScope();
    var dockerService = scope.ServiceProvider.GetRequiredService<IDockerLifecycleService>();
    await dockerService.RunCombineAndMergeAsync();
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();