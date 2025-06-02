namespace WebCrawler.Helpers
{
    public static class LoggerHelper
    {
        private static readonly Lock _logFileLock = new();

        public static void LogToFile(string message)
        {
            var objDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "obj");
            var logPath = Path.Combine(objDir, "crawl.log");
            var logLine = $"[{DateTime.Now:O}] {message}{Environment.NewLine}";

            lock (_logFileLock)
            {
                // Ensure the directory exists before writing
                if (!Directory.Exists(objDir))
                    Directory.CreateDirectory(objDir);

                File.AppendAllText(logPath, logLine);
            }

            // Always log to console for Docker visibility
            Console.WriteLine(logLine);
        }
    }
}