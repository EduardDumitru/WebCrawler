using FuzzySharp;

namespace WebCrawler.Helpers
{
    public static class SearchHelper
    {
        // Helper for best fuzzy match among multiple fields
        public static int MaxFuzzy(string? input, IEnumerable<string?> fields)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;
            return fields
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => Fuzz.Ratio(input, f!))
                .DefaultIfEmpty(0)
                .Max();
        }

        // Helper for best fuzzy match in pipe-delimited fields (for CompanyAllAvailableNames)
        public static int MaxFuzzyPipeOrSingle(string? input, string? value)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(value))
                return 0;
            // If pipe is present, split and compare each part
            if (value.Contains('|'))
            {
                var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Select(p => Fuzz.Ratio(input, p)).DefaultIfEmpty(0).Max();
            }
            // Otherwise, compare directly
            return Fuzz.Ratio(input, value);
        }

        // Helper for best fuzzy match in long/text fields (token set ratio). Useful for addresses search. But we are not using it right now, as it is not needed for the current search requirements.
        //static int MaxFuzzyTokenSet(string? input, string? pipeDelimited)
        //{
        //    if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(pipeDelimited))
        //        return 0;
        //    var parts = pipeDelimited.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        //    return parts.Select(p => Fuzz.TokenSetRatio(input, p)).DefaultIfEmpty(0).Max();
        //}

        public static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var normalized = url.Trim().ToLowerInvariant();
            while (normalized.StartsWith("http://") || normalized.StartsWith("https://") || normalized.StartsWith("https//") || normalized.StartsWith("http//"))
            {
                if (normalized.StartsWith("https://"))
                    normalized = normalized.Substring(8);
                else if (normalized.StartsWith("http://"))
                    normalized = normalized.Substring(7);
                else if (normalized.StartsWith("https//"))
                    normalized = normalized.Substring(7);
                else if (normalized.StartsWith("http//"))
                    normalized = normalized.Substring(6);
            }
            return normalized.TrimEnd('/');
        }

        public static IEnumerable<string> NormalizePhoneVariants(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                yield break;

            var digits = new string([.. phone.Where(char.IsDigit)]);
            // If 10 digits, assume US and prepend '1'
            if (digits.Length == 10)
                digits = "1" + digits;

            yield return digits;           // Without '+'
            yield return "+" + digits;     // With '+'
        }

        public static string NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            // Remove all non-alphanumeric characters and convert to lower case
            var normalized = new string(name
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray());
            // Collapse multiple spaces
            return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}