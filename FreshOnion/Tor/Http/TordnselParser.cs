using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FreshOnion.Tor.Http;

public static class TordnselParser
{
    // Expected date formats found in the tordnsel file
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss zzz"
    };

    private static readonly Regex ExitAddressIpRegex = new Regex(
        @"^\s*(?<ip>[\w.:]+?)(?:\s+.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses a tordnsel 1.0 exit list document.
    /// Returns true if at least one valid node was parsed.
    /// Each node may have multiple ExitAddress lines; all are collected.
    /// </summary>
    public static bool TryParse(string content, out List<ExitNodeInfo> result)
    {
        result = new List<ExitNodeInfo>();

        if (string.IsNullOrWhiteSpace(content))
            return false;

        // State for the current node being built
        string currentExitNode = string.Empty;
        DateTimeOffset currentPublished = default;
        DateTimeOffset currentLastStatus = default;
        var pendingAddresses = new List<IPAddress>();

        void FlushNode(List<ExitNodeInfo> nodesResult)
        {
            if (string.IsNullOrEmpty(currentExitNode) || pendingAddresses.Count == 0)
                return; // No complete node to flush, or no addresses

            // Emit one ExitNodeInfo per exit address
            foreach (var ip in pendingAddresses)
            {
                nodesResult.Add(new ExitNodeInfo(currentExitNode, currentPublished, currentLastStatus, ip));
            }

            // Reset for the next node
            currentExitNode = string.Empty;
            currentPublished = default;
            currentLastStatus = default;
            pendingAddresses.Clear();
        }

        foreach (var rawLine in content.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();

            if (line.IsEmpty || line.StartsWith("@type", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("ExitNode ", StringComparison.Ordinal))
            {
                // Flush the previous node before starting a new one
                FlushNode(result);

                currentExitNode = line.Slice("ExitNode ".Length).ToString().Trim();
                currentPublished = default;
                currentLastStatus = default;
                pendingAddresses.Clear();
            }
            else if (line.StartsWith("Published ", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(currentExitNode)) continue; // Must have an ExitNode first
                var val = line.Slice("Published ".Length).ToString().Trim();
                TryParseDate(val, out currentPublished);
            }
            else if (line.StartsWith("LastStatus ", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(currentExitNode)) continue; // Must have an ExitNode first
                var val = line.Slice("LastStatus ".Length).ToString().Trim();
                TryParseDate(val, out currentLastStatus);
            }
            else if (line.StartsWith("ExitAddress ", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(currentExitNode)) continue; // Must have an ExitNode first
                // Format: ExitAddress <ip> <date> (date is ignored in this implementation based on current ExitNodeInfo)
                var rest = line.Slice("ExitAddress ".Length).ToString().Trim();
                var spaceIdx = rest.IndexOf(' ');
                
                string ipStr;
                // If there's a space, it means there's an IP and possibly a date.
                // If no space, the whole thing is assumed to be the IP.
                if (spaceIdx > 0)
                {
                    ipStr = rest[..spaceIdx].Trim();
                    // var dateStr = rest[(spaceIdx + 1)..].Trim(); // Date is not currently used in ExitNodeInfo constructor
                }
                else
                {
                    ipStr = rest;
                }

                if (IPAddress.TryParse(ipStr, out var ip))
                {
                    pendingAddresses.Add(ip);
                }
            }
            // Unknown line kinds are silently ignored
        }

        // Flush the final node
        FlushNode(result);

        return result.Count > 0;
    }

    private static bool TryParseDate(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParseExact(
            value,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out result);
    }
}