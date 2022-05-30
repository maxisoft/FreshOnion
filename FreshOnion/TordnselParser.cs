using System.Globalization;
using System.Text.RegularExpressions;

namespace FreshOnion;

public static class TordnselParser
{
    private static readonly Regex LineRegex =
        new Regex(
            @"^(?<kind>(?:ExitNode)|(?:Published)|(?:LastStatus)|(?:ExitAddress)|(?:@type)|(?:Downloaded))\s+(?<value>.*?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static bool TryParse(string input, out List<ExitNodeInfo> result)
    {
        result = new List<ExitNodeInfo>();
        var matches = LineRegex.Matches(input);
        if (matches.Count < 2) return false;
        if (matches.Take(2).Any(match => !match.Success))
        {
            return false;
        }

        if (matches[0].Groups["kind"].Value != "@type") return false;
        if (matches[1].Groups["kind"].Value != "Downloaded") return false;

        var node = new ExitNodeInfo();
        foreach (var match in matches.Skip(2))
        {
            if (!match.Success) return false;
            var value = match.Groups["value"].ValueSpan;
            switch (match.Groups["kind"].Value)
            {
                case "ExitNode":
                    if (!string.IsNullOrEmpty(node.ExitNode))
                    {
                        result.Add(node);
                    }
                    else if (result.Any())
                    {
                        return false;
                    }

                    node = new ExitNodeInfo { ExitNode = value.ToString() };
                    break;
                case "Published":
                    if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var published))
                    {
                        return false;
                    }

                    node = node with { Published = published.LocalDateTime };
                    break;
                case "LastStatus":
                    if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var lastStatus))
                    {
                        return false;
                    }

                    node = node with { LastStatus = lastStatus.LocalDateTime };
                    break;
                case "ExitAddress":
                    node = node with { ExitAddress = value.ToString() };
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}