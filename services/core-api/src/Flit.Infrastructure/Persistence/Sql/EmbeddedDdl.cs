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
