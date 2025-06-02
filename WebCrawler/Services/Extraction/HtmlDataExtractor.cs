using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PhoneNumbers;
using static WebCrawler.Services.RegularExpressionHelper;

namespace WebCrawler.Services.Extraction
{
    public static class HtmlDataExtractor
    {
        private const int MaxAddressRecursionDepth = 12;

        private static readonly string[] parkingDomains =
        [
            "sedoparking.com", "afternic.com", "dan.com", "hugedomains.com", "bodis.com", "uniregistry.com",
                "buydomains.com", "domainmarket.com", "porkbun.com/market", "namecheap.com/domains/marketplace",
                    "godaddy.com/domain-auctions", "sav.com", "parkingcrew.net", "undeveloped.com", "above.com",
                    "parking.com", "domainnamesales.com", "domainagents.com", "flippa.com", "snapnames.com",
                    "dropcatch.com", "aftermarket.com"
        ];

        private static readonly string[] parkedPatterns =
        [
            "this domain is for sale", "buy this domain", "domain is available", "get this domain",
            "domain parked by", "domain parking", "domain marketplace", "interested in this domain"
        ];

        private static readonly string[] socialBases =
        [
            "https://www.facebook.com/", "https://facebook.com/", "https://x.com/", "https://www.x.com/",
            "https://instagram.com/", "https://www.instagram.com/", "https://www.linkedin.com/",
            "https://linkedin.com/", "https://twitter.com/", "https://www.twitter.com/",
            "http://www.facebook.com/", "http://facebook.com/", "http://x.com/", "http://www.x.com/",
            "http://instagram.com/", "http://www.instagram.com/", "http://www.linkedin.com/",
            "http://linkedin.com/", "http://twitter.com/", "http://www.twitter.com/"
        ];

        private static readonly string[] AddressNoiseKeywords =
        [
            "created with", "service", "facility", "why us", "location", "contact",
            "blog", "more", "navigate", "menu items", "copyright", "proudly", "openstreetmap"
        ];

        private static readonly char[] separator = ['\r', '\n'];

        public static IList<string?>? ExtractPhones(HtmlNode bodyNode, string cleanedHtml)
        {
            var anchorPhones = ExtractPhoneNumbersFromAnchors(bodyNode);
            var candidates = GetPhoneCandidates(anchorPhones, cleanedHtml);
            return ValidatePhoneNumbers(candidates);
        }

        public static List<string> ExtractPhoneNumbersFromAnchors(HtmlNode bodyNode)
        {
            var results = new List<string>();
            var anchorNodes = bodyNode.SelectNodes(".//a[@href]");
            if (anchorNodes == null) return results;

            foreach (var link in anchorNodes)
            {
                var href = link.GetAttributeValue("href", "");
                if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                    results.Add(href.Replace("tel:", "").Trim());

                var innerText = link.InnerText?.Trim();
                if (!string.IsNullOrEmpty(innerText) && innerText.Length < 25)
                    results.Add(innerText);
            }
            return results;
        }

        public static List<string> ExtractEmailsFromAnchors(HtmlNode bodyNode)
        {
            var results = new List<string>();
            var anchorNodes = bodyNode.SelectNodes(".//a[@href]");
            if (anchorNodes == null) return results;

            foreach (var link in anchorNodes)
            {
                var href = link.GetAttributeValue("href", "");
                if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    results.Add(href.Replace("mailto:", "").Trim());
            }
            return results;
        }

        public static List<string> ExtractAddressesFromBody(HtmlNode bodyNode, List<string> phones, List<string> emails)
        {
            var addresses = new List<string>();

            // Get all potential address nodes
            var addressNodes = GetPotentialAddressNodes(bodyNode);

            // Extract from address nodes
            foreach (var node in addressNodes)
            {
                if (node.GetAttributeValue("itemtype", "") == "http://schema.org/PostalAddress")
                {
                    ExtractStructuredPostalAddress(node, addresses);
                }
                else
                {
                    // Generic extraction
                    addresses.AddRange(ExtractAddresses(node));
                }
            }

            // Look near phones and emails for more addresses
            ExtractAddressesNearPhonesAndEmails(bodyNode, phones, emails, addresses);

            return addresses;
        }

        public static void CleanHtmlBody(HtmlNode bodyNode)
        {
            // Remove <style>, <script>, and <svg> nodes from the body
            foreach (var node in bodyNode.SelectNodes(".//style|.//script|.//svg") ?? Enumerable.Empty<HtmlNode>())
            {
                node.Remove();
            }
        }

        public static List<string?>? ValidatePhoneNumbers(List<string> candidates)
        {
            var phoneUtil = PhoneNumberUtil.GetInstance();

            var foundNumbers = candidates
                .AsParallel()
                .WithDegreeOfParallelism(3)
                .Select(candidate =>
                {
                    try
                    {
                        string normalized = candidate;
                        if (phoneUtil.IsAlphaNumber(candidate))
                        {
                            normalized = ConvertAlphaToNumeric(candidate);
                        }

                        PhoneNumber? number = null;
                        try
                        {
                            number = phoneUtil.Parse(normalized, "US");
                            if (number != null && phoneUtil.IsValidNumber(number))
                            {
                                return phoneUtil.Format(number, PhoneNumberFormat.E164);
                            }
                        }
                        catch (NumberParseException)
                        {
                            /* Ignore parsing errors */
                        }
                    }
                    catch { /* Ignore validation errors */ }
                    return null;
                })
                .Where(formatted => !string.IsNullOrEmpty(formatted))
                .Distinct()
                .ToList();

            return foundNumbers;
        }

        public static (List<string> SocialLinks, List<string> MapAddresses) ExtractLinksFromBody(HtmlNode bodyNode)
        {
            var socialLinks = new List<string>();
            var mapAddresses = new List<string>();

            var links = bodyNode.SelectNodes(".//a[@href]");
            if (links == null)
                return (socialLinks, mapAddresses);

            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", "");

                // Process social media links
                if (IsSocialMediaLink(href))
                {
                    socialLinks.Add(href);
                }

                // Process map links
                if (IsMapLink(href))
                {
                    ExtractAddressFromMap(link, href, mapAddresses);
                }
            }

            return (socialLinks, mapAddresses);
        }

        public static bool IsParkedDomain(string url)
        {
            return parkingDomains.Any(pd => url.Contains(pd, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsParkedContent(string html)
        {
            return !string.IsNullOrWhiteSpace(html) &&
                   parkedPatterns.Any(p => html.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasValidBody(string html)
        {
            return !string.IsNullOrWhiteSpace(html) &&
                   html.Contains("<body", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> GetPhoneCandidates(List<string> anchorPhones, string cleanedHtml)
        {
            // Combine anchors with regex matches from text
            return [.. anchorPhones
                .Concat(RegularExpressionHelper.PhoneRegex.PhoneRegexValidation().Matches(cleanedHtml).Select(m => m.Value))
                .Select(s => s.Trim())
                .Where(s =>
                    s.Count(char.IsDigit) >= 7 &&
                    RegularExpressionHelper.PhoneRegex.PhoneAlphaNumberRegexValidation().IsMatch(s) &&
                    !RegularExpressionHelper.PhoneRegex.LettersBetweenDigitsRegexValidation().IsMatch(s) &&
                    !RegularExpressionHelper.PhoneRegex.SpaceBeforeTrailingLettersRegexValidation().IsMatch(s) &&
                    s.All(c => char.IsLetterOrDigit(c) || " -+()".Contains(c))
                      )
                .Distinct()];
        }

        private static List<HtmlNode> GetPotentialAddressNodes(HtmlNode bodyNode)
        {
            var nodes = new List<HtmlNode>();

            // Footer
            var footer = bodyNode.SelectSingleNode(".//footer");
            if (footer != null) nodes.Add(footer);

            // <address> tags
            nodes.AddRange(bodyNode.SelectNodes(".//address") ?? Enumerable.Empty<HtmlNode>());

            // Elements with address-related classes/IDs
            nodes.AddRange(bodyNode.SelectNodes(
                ".//*[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address') or " +
                "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address') or " +
                "contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'location') or " +
                "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'location') or " +
                "contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'contact') or " +
                "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'contact') or " +
                "contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'office') or " +
                "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'office')]"
                                               ) ?? Enumerable.Empty<HtmlNode>());

            // Elements with data attributes
            nodes.AddRange(bodyNode.SelectNodes(
                ".//*[contains(translate(@data-route, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address') or " +
                "contains(translate(@data-aid, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address') or " +
                "contains(translate(@data-address, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address') or " +
                "contains(translate(@aria-label, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'address')]"
                                               ) ?? Enumerable.Empty<HtmlNode>());

            // Schema.org microdata
            nodes.AddRange(bodyNode.SelectNodes(
                ".//*[@itemprop='address' or @itemtype='http://schema.org/PostalAddress']"
                                               ) ?? Enumerable.Empty<HtmlNode>());

            // Header and navigation elements
            nodes.AddRange(bodyNode.SelectNodes(".//header") ?? Enumerable.Empty<HtmlNode>());
            nodes.AddRange(
                bodyNode.SelectNodes(
                    ".//div[contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'header') or " +
                    "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'header') or " +
                    "contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'top-bar') or " +
                    "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'top-bar') or " +
                    "contains(translate(@class, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'navbar') or " +
                    "contains(translate(@id, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'navbar')]"
                                    ) ?? Enumerable.Empty<HtmlNode>()
                          );

            // Special __typename attributes
            nodes.AddRange(bodyNode.SelectNodes(".//div[@__typename='Address']") ?? Enumerable.Empty<HtmlNode>());
            nodes.AddRange(bodyNode.SelectNodes(".//div[@__typename='address']") ?? Enumerable.Empty<HtmlNode>());

            return nodes.Distinct().ToList();
        }

        private static void ExtractStructuredPostalAddress(HtmlNode node, List<string> addresses)
        {
            var parts = new[]
            {
                node.SelectSingleNode(".//*[@itemprop='name']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='streetAddress']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='postOfficeBoxNumber']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='addressLine1']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='addressLine2']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='addressLocality']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='addressRegion']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='postalCode']")?.InnerText?.Trim(),
                node.SelectSingleNode(".//*[@itemprop='addressCountry']")?.InnerText?.Trim()
            };

            var address = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
            if (!string.IsNullOrWhiteSpace(address))
            {
                addresses.Add(address);
            }
        }

        private static void ExtractAddressesNearPhonesAndEmails(
            HtmlNode bodyNode, List<string> phones, List<string> emails, List<string> addresses)
        {
            // Look near phone numbers
            foreach (var phone in phones)
            {
                var phoneNode = bodyNode.SelectSingleNode($".//*[contains(text(), '{phone}')]");
                if (phoneNode != null)
                {
                    // Check parent node
                    if (phoneNode.ParentNode != null)
                    {
                        addresses.AddRange(ExtractAddresses(phoneNode.ParentNode));
                    }

                    // Check sibling nodes
                    foreach (var sibling in phoneNode.ParentNode?.ChildNodes ?? Enumerable.Empty<HtmlNode>())
                    {
                        if (sibling != phoneNode)
                        {
                            addresses.AddRange(ExtractAddresses(sibling));
                        }
                    }
                }
            }

            // Look near emails
            foreach (var email in emails)
            {
                var emailNode = bodyNode.SelectSingleNode($".//*[contains(text(), '{email}')]");
                if (emailNode != null)
                {
                    // Check parent node
                    if (emailNode.ParentNode != null)
                    {
                        addresses.AddRange(ExtractAddresses(emailNode.ParentNode));
                    }

                    // Check sibling nodes
                    foreach (var sibling in emailNode.ParentNode?.ChildNodes ?? Enumerable.Empty<HtmlNode>())
                    {
                        if (sibling != emailNode)
                        {
                            addresses.AddRange(ExtractAddresses(sibling));
                        }
                    }
                }
            }
        }

        private static List<string> ExtractAddresses(HtmlNode node)
        {
            var addresses = new List<string>();
            ExtractAddressesRecursive(node, addresses, 0);
            return addresses;
        }

        private static void ExtractAddressesRecursive(HtmlNode node, List<string> addresses, int depth = 0)
        {
            if (depth > MaxAddressRecursionDepth)
                return;

            if (!node.HasChildNodes && !string.IsNullOrWhiteSpace(node.InnerText))
            {
                var text = node.InnerText.Trim();

                foreach (Match match in AddressRegex.USAddressRegexValidation().Matches(text))
                {
                    var value = match.Value.Trim();

                    if (value.Length > 80 && AddressNoiseKeywords.Any(k =>
                        text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    addresses.Add(value);
                }
            }
            else
            {
                foreach (var child in node.ChildNodes)
                {
                    ExtractAddressesRecursive(child, addresses, depth + 1);
                }
            }
        }

        private static string ConvertAlphaToNumeric(string input)
        {
            var map = new Dictionary<char, char>
            {
                ['A'] = '2',
                ['B'] = '2',
                ['C'] = '2',
                ['D'] = '3',
                ['E'] = '3',
                ['F'] = '3',
                ['G'] = '4',
                ['H'] = '4',
                ['I'] = '4',
                ['J'] = '5',
                ['K'] = '5',
                ['L'] = '5',
                ['M'] = '6',
                ['N'] = '6',
                ['O'] = '6',
                ['P'] = '7',
                ['Q'] = '7',
                ['R'] = '7',
                ['S'] = '7',
                ['T'] = '8',
                ['U'] = '8',
                ['V'] = '8',
                ['W'] = '9',
                ['X'] = '9',
                ['Y'] = '9',
                ['Z'] = '9'
            };

            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var c in input.ToUpperInvariant())
            {
                if (map.TryGetValue(c, out var digit))
                    sb.Append(digit);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool IsSocialMediaLink(string href)
        {
            // Skip processing if empty
            if (string.IsNullOrEmpty(href))
                return false;

            string normalizedHref = href.TrimEnd('/', '#');

            // Excluded patterns
            if (IsMainSocialPage(normalizedHref) || IsExcludedSocialPath(href))
                return false;

            // Include only social media domains
            return IsSocialMediaDomain(href);
        }

        private static bool IsMainSocialPage(string normalizedHref)
        {
            return socialBases.Any(baseUrl =>
                normalizedHref.Equals(baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsExcludedSocialPath(string href)
        {
            return href.Contains("/posts/", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/sharer/sharer.php", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/shareArticle", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/intent/tweet", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/share?", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/jobs/", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/p/", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/reel/", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/status/", StringComparison.OrdinalIgnoreCase) ||
                   href.EndsWith("/wix", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/wix-com?", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/wix-com/", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("facebook.com/groups") && href.Contains("files");
        }

        private static bool IsSocialMediaDomain(string href)
        {
            return href.Contains("facebook.com", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/x.com", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("/www.x.com", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("instagram.com", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) ||
                   href.Contains("twitter.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMapLink(string href)
        {
            return href.Contains("maps.google.com") ||
                   href.Contains("goo.gl/maps") ||
                   href.Contains("apple.com/maps") ||
                   href.Contains("bing.com/maps") ||
                   href.Contains("here.com") ||
                   href.Contains("mapquest.com") ||
                   href.Contains("waze.com/ul");
        }

        private static void ExtractAddressFromMap(HtmlNode link, string href, List<string> addresses)
        {
            // Try to extract from anchor text
            var text = link.InnerText?
                .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (text != null && text.Length > 0)
            {
                var joined = string.Join(", ", text);
                if (joined.Length > 5)
                {
                    addresses.Add($"{joined} ({href})");
                    return;
                }
            }

            // Fallback to URL parameters
            try
            {
                var uri = new UriBuilder(href);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var q = query["q"] ?? query["where"] ?? query["address"] ??
                        query["destination"] ?? query["ll"];

                if (!string.IsNullOrEmpty(q))
                {
                    addresses.Add($"{q} ({href})");
                }
                else
                {
                    // Last resort: just store the URL
                    addresses.Add(href);
                }
            }
            catch
            {
                addresses.Add(href);
            }
        }
    }
}