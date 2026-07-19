using System.Text.RegularExpressions;

namespace CheckMind.App.Core;

public static class NotchProfileIndexResolver
{
    public const string TaskFieldName = "notchProfileCount";
    public const string CountEnvName = "CHECKMIND_NOTCH_PROFILE_COUNT";
    public const string IndexEnvName = "CHECKMIND_NOTCH_PROFILE_INDEX";
    public const string IndexesEnvName = "CHECKMIND_NOTCH_PROFILE_INDEXES";

    public static IReadOnlyList<int> ResolveIndexesFromEnvironment()
    {
        var rawIndexes = GetTrimmedEnv(IndexesEnvName);
        if (!string.IsNullOrWhiteSpace(rawIndexes))
        {
            return ParseDistinctPositiveIndexes(rawIndexes);
        }

        var rawIndex = GetTrimmedEnv(IndexEnvName);
        if (!string.IsNullOrWhiteSpace(rawIndex))
        {
            return ParseDistinctPositiveIndexes(rawIndex);
        }

        var count = ResolveCountFromEnvironment();
        if (!count.HasValue)
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(1, count.Value).ToArray();
    }

    public static int? ResolveCountFromEnvironment()
    {
        var rawCount = GetTrimmedEnv(CountEnvName);
        if (string.IsNullOrWhiteSpace(rawCount))
        {
            rawCount = GetTrimmedEnv(TaskFieldName);
        }

        if (string.IsNullOrWhiteSpace(rawCount))
        {
            return null;
        }

        if (!int.TryParse(rawCount, out var count) || count <= 0)
        {
            throw new InvalidOperationException(
                $"非法 {TaskFieldName} / {CountEnvName}：{rawCount}");
        }

        return count;
    }

    private static IReadOnlyList<int> ParseDistinctPositiveIndexes(string raw)
    {
        var values = new List<int>();
        foreach (Match match in Regex.Matches(raw, @"\d+"))
        {
            if (!int.TryParse(match.Value, out var index) || index <= 0)
            {
                throw new InvalidOperationException($"非法 Notch Profile 序号：{match.Value}");
            }

            if (!values.Contains(index))
            {
                values.Add(index);
            }
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException($"非法 Notch Profile 序号输入：{raw}");
        }

        return values;
    }

    private static string GetTrimmedEnv(string name)
        => (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
}
