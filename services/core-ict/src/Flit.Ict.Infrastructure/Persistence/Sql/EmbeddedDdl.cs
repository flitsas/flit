using System.Reflection;
using System.Text;

namespace Flit.Ict.Infrastructure.Persistence.Sql;

/// <summary>
/// Carga los scripts DDL embebidos del schema <c>ict</c>. Calca el EmbeddedDdl de core-api,
/// ajustando el prefijo de recurso a este ensamblado.
/// </summary>
internal static class EmbeddedDdl
{
    private const string ResourcePrefix = "Flit.Ict.Infrastructure.Persistence.Sql.Ddl.";

    public static string LoadUp(string scriptFileName)
    {
        var assembly = typeof(EmbeddedDdl).Assembly;
        var resourceName = ResourcePrefix + scriptFileName;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"DDL resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Nombres de todos los scripts DDL a aplicar, en orden.</summary>
    public static IReadOnlyList<string> AllScriptsInOrder()
    {
        var assembly = typeof(EmbeddedDdl).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
