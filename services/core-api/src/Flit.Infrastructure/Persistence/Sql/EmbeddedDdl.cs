using System.Reflection;
using System.Text;

namespace Flit.Infrastructure.Persistence.Sql;

/// <summary>
/// Loads phase-1 DDL scripts embedded as resources for EF migrations HU #10148–#10153.
/// </summary>
internal static class EmbeddedDdl
{
    private const string ResourcePrefix = "Flit.Infrastructure.Persistence.Sql.Ddl.";

    public static string LoadUp(string scriptFileName)
    {
        var sql = LoadResource(scriptFileName);
        return StripHeaderComments(sql);
    }

    /// <summary>
    /// HU #13641 / Feature #13105: scripts del marcador <c>security.permissions.enforced</c> (nombre con <c>enforced</c>),
    /// en orden numérico. El 139 clasifica el catálogo; cada HU que instrumenta un módulo agrega el suyo.
    /// </summary>
    public static IReadOnlyList<string> EnforcedMarkerScripts() =>
        typeof(EmbeddedDdl).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.Contains("enforced", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..])
            .OrderBy(n => int.Parse(n[..n.IndexOf('-')], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    private static string LoadResource(string scriptFileName)
    {
        var assembly = typeof(EmbeddedDdl).Assembly;
        var resourceName = ResourcePrefix + scriptFileName;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"DDL resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string StripHeaderComments(string sql)
    {
        var lines = sql.Split('\n');
        var start = 0;
        while (start < lines.Length && lines[start].TrimStart().StartsWith("--", StringComparison.Ordinal))
        {
            start++;
        }

        return string.Join('\n', lines.Skip(start));
    }
}
