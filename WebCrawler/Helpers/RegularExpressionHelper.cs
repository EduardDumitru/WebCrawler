using System.Text.RegularExpressions;

namespace WebCrawler.Services
{
    public static partial class RegularExpressionHelper
    {
        public static partial class PhoneRegex
        {
            // This regex matches international and North American formats, but is less likely to match dates
            [GeneratedRegex(@"(?<!\w)(\+?\d[\d\-\s\(\)]{6,}\d(?:[A-Za-z]+)?)\b", RegexOptions.Compiled)]
            public static partial Regex PhoneRegexValidation();

            [GeneratedRegex(@"\d{3,}", RegexOptions.Compiled)]
            public static partial Regex PhoneAlphaNumberRegexValidation();

            [GeneratedRegex(@"\d+[A-Za-z]+\d+", RegexOptions.Compiled)]
            public static partial Regex LettersBetweenDigitsRegexValidation();

            [GeneratedRegex(@"\s+[A-Za-z]+$", RegexOptions.Compiled)]
            public static partial Regex SpaceBeforeTrailingLettersRegexValidation();
        }

        public static partial class AddressRegex
        {
            [GeneratedRegex(@"\d{1,6}\s+[\w\s\.\-\,]+(street|st\.|road|rd\.|avenue|ave|blvd|drive|dr|lane|ln|court|ct|circle|cir|highway|hwy|parkway|pkwy)[\w\s\.\-\,]*\d{5}(-\d{4})?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
            public static partial Regex USAddressRegexValidation();
        }
    }
}