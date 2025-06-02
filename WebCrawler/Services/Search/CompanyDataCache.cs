using System.Globalization;
using CsvHelper;
using WebCrawler.Core.Models;

namespace WebCrawler.Services.Search
{
    public class CompanyDataCache
    {
        public List<CompanyExtended> Companies { get; }

        public CompanyDataCache()
        {
            using var reader = new StreamReader("results/Companies-Data.csv");
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            Companies = [.. csv.GetRecords<CompanyExtended>()];
        }
    }
}