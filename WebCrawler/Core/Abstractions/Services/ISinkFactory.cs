using WebCrawler.Core.Models;

namespace WebCrawler.Core.Abstractions.Services
{
    public interface ISinkFactory
    {
        ISink GetSink(SinkType sinkType, ServiceScope scope = ServiceScope.Singleton);
    }
}