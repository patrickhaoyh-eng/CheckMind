namespace CheckMind.App.Core;

public static class OcrId
{
    public static string Make(string prefix, string key)
    {
        var raw = $"{prefix}_{key}";
        return Sanitize(raw);
    }

    public static string Sanitize(string raw)
    {
        Span<char> buffer = stackalloc char[raw.Length];
        var j = 0;
        foreach (var c in raw)
        {
            if ((c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_' ||
                c == '-' ||
                c == '.')
            {
                buffer[j++] = c;
                continue;
            }

            buffer[j++] = '_';
        }

        return new string(buffer[..j]);
    }
}

