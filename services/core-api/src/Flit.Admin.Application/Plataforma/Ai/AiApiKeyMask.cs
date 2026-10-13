namespace Flit.Admin.Application.Plataforma.Ai;

public static class AiApiKeyMask
{
    public static string? FromKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var trimmed = rawKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "***";
        }

        return $"{trimmed[..3]}***{trimmed[^4..]}";
    }
}
