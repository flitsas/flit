using System.Text.Json;

namespace Flit.Consultas.Api.Simulacion;

/// <summary>
/// HU #14153 AC3 — respuestas crudas INVENTADAS de los proveedores de conductor (Kyverum RUNT persona) y RUES (Verifik), versionadas
/// como recursos del ensamblado (<c>Simulacion/Fixtures/proveedor-*.json</c>), con la forma del JSON real según los DTO del módulo
/// (<c>KyverumRuntPersonaResponse</c>, <c>VerifikRuesResponse</c>). Datos ficticios: documentos y NIT en el rango 99999…, nombres
/// «FICTICI…». Nunca se construyen a partir de una muestra real. Las marcas <c>{{documento}}</c>, <c>{{nit}}</c>, <c>{{estado}}</c> y
/// <c>{{renovacion}}</c> se reemplazan con el valor consultado (escapado como texto JSON) por <see cref="Con"/>.
/// </summary>
internal static class FixturesProveedor
{
    public const string Conductor = "proveedor-kyverum-conductor.json";
    public const string ConductorSinLicencias = "proveedor-kyverum-conductor-sin-licencias.json";
    public const string Rues = "proveedor-verifik-rues.json";

    private static readonly string Prefijo = $"{typeof(FixturesProveedor).Assembly.GetName().Name}.Simulacion.Fixtures.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Cargados = new(Cargar);

    /// <summary>Nombre del archivo → contenido JSON (con las marcas sin reemplazar).</summary>
    public static IReadOnlyDictionary<string, string> Todos() => Cargados.Value;

    /// <summary>El fixture <paramref name="archivo"/> con cada marca <c>{{clave}}</c> reemplazada por su valor como texto JSON.</summary>
    public static string Con(string archivo, params (string Clave, string? Valor)[] valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        var json = Todos()[archivo];
        foreach (var (clave, valor) in valores)
        {
            // La marca ya va entre comillas en el fixture: se inserta el valor escapado como JSON, sin sus comillas.
            var escapado = JsonSerializer.Serialize(valor ?? string.Empty)[1..^1];
            json = json.Replace("{{" + clave + "}}", escapado, StringComparison.Ordinal);
        }

        return json;
    }

    private static Dictionary<string, string> Cargar()
    {
        var ensamblado = typeof(FixturesProveedor).Assembly;
        var fixtures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recurso in ensamblado.GetManifestResourceNames().Where(n => n.StartsWith(Prefijo + "proveedor-", StringComparison.Ordinal)))
        {
            using var stream = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(stream);
            fixtures[recurso[Prefijo.Length..]] = lector.ReadToEnd();
        }

        return fixtures;
    }
}
