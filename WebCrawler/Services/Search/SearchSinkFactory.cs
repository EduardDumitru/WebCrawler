using WebCrawler.Core.Abstractions.Services;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Search
{
    public class SinkFactory(IServiceProvider serviceProvider) : ISinkFactory
    {
        public ISink GetSink(SinkType sinkType, ServiceScope scope = ServiceScope.Singleton)
        {
            if (scope == ServiceScope.Scoped)
            {
                // For Scoped services, we need to create a scope to resolve the service
                using var scopeService = serviceProvider.CreateScope();
                return sinkType switch
                {
                    _ => throw new ArgumentException($"Unknown sink type: {sinkType}")
                };
            }

            return sinkType switch
            {
                SinkType.ElasticSearch => serviceProvider.GetRequiredService<ElasticSearchSink>(),
                // Add more as needed
                _ => throw new ArgumentException($"Unknown sink type: {sinkType}")
            };
        }
    }
}