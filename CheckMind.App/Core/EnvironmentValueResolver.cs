namespace CheckMind.App.Core;

public static class EnvironmentValueResolver
{
    public static string? Get(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return null;
        }

        return FirstNonEmpty(
            Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Process),
            Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine)
        );
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

