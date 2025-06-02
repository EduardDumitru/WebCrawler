namespace WebCrawler.Helpers
{
    public static class RobotsChecker
    {
        /// <summary>
        /// Checks if crawling is allowed for the given domain and path by robots.txt.
        /// </summary>
        public static async Task<bool> IsAllowedAsync(
            string domain,
            HttpClient httpClient,
            string userAgent = "*",
            string path = "/",
            CancellationToken cancellationToken = default)
        {
            try
            {
                var robotsTxtUrl = $"https://{domain}/robots.txt";
                LoggerHelper.LogToFile($"[HTTP] Requesting {robotsTxtUrl} at {DateTime.Now:O}");
                var response = await httpClient.GetAsync(robotsTxtUrl, cancellationToken);
                LoggerHelper.LogToFile($"[HTTP] Response for {robotsTxtUrl} at {DateTime.Now:O} - Status: {(int)response.StatusCode}");

                var robotsTxtContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return IsPathAllowedByRobots(robotsTxtContent, userAgent, path);
            }
            catch (Exception ex)
            {
                LoggerHelper.LogToFile($"Error checking robots for domain {domain}: {ex}");
                // If robots.txt check fails, allow crawling by default
                return true;
            }
        }

        /// <summary>
        /// Determines if the given path is allowed by the robots.txt content for the specified user agent.
        /// </summary>
        private static bool IsPathAllowedByRobots(string robotsTxtContent, string userAgent, string path)
        {
            var lines = robotsTxtContent.Split('\n');
            bool applies = false;
            var disallows = new List<string>();
            var allows = new List<string>();

            foreach (var line in lines.Select(l => l.Trim()))
            {
                if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    applies = line.Substring(11).Trim() == "*" ||
                              line.Substring(11).Trim().Equals(userAgent, StringComparison.OrdinalIgnoreCase);
                }
                else if (applies && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    var rule = line.Substring(9).Trim();
                    if (!string.IsNullOrEmpty(rule)) disallows.Add(rule);
                }
                else if (applies && line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                {
                    var rule = line.Substring(6).Trim();
                    if (!string.IsNullOrEmpty(rule)) allows.Add(rule);
                }
                else if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    applies = false;
                }
            }

            // Check allows first (most specific rule wins)
            if (allows.Any(a => path.StartsWith(a)))
                return true;
            if (disallows.Any(d => path.StartsWith(d)))
                return false;
            return true;
        }
    }
}