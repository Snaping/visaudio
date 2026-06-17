using System.IO;
using System.Text.RegularExpressions;
using VisAudio.Models;

namespace VisAudio.Services;

public static class LrcParser
{
    private static readonly Regex TimestampPattern = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex MetadataPattern = new(@"^\[(?:ar|ti|al|by|offset|re|ve):", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<LrcLine> Parse(string filePath)
    {
        var result = new List<LrcLine>();

        try
        {
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (MetadataPattern.IsMatch(trimmed))
                    continue;

                var matches = TimestampPattern.Matches(trimmed);
                if (matches.Count == 0)
                    continue;

                var textPart = TimestampPattern.Replace(trimmed, "").Trim();

                foreach (Match match in matches)
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);

                    TimeSpan timestamp;
                    var msGroup = match.Groups[3];
                    if (msGroup.Success)
                    {
                        string msStr = msGroup.Value.PadRight(3, '0');
                        int milliseconds = int.Parse(msStr);
                        timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                    }
                    else
                    {
                        timestamp = new TimeSpan(0, 0, minutes, seconds);
                    }

                    result.Add(new LrcLine { Timestamp = timestamp, Text = textPart });
                }
            }

            result.Sort();
        }
        catch
        {
            return new List<LrcLine>();
        }

        return result;
    }
}
