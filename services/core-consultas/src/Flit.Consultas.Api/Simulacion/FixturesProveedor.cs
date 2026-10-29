using System.Text.Json;

namespace Flit.Consultas.Api.Simulacion;

/// <summary>
/// HU #14153 AC3 y HU #14183 T2 — respuestas crudas INVENTADAS de conductor (Kyverum), RUES (Verifik) y SIMIT (Verifik),
/// versionadas como recursos del ensamblado (<c>Simulacion/Fixtures/proveedor-*.json</c>). SIMIT usa los cuerpos de la sección 3.1
/// del plan (persona con cobro coactivo, vehículo sin multas y 404 sin registros): documentos 99999… y placas TST201/TST202.
/// Nunca se construyen a partir de una muestra con identificadores reales. Las marcas <c>{{documento}}</c>, <c>{{nit}}</c>,
/// <c>{{estado}}</c> y <c>{{renovacion}}</c> se reemplazan con el valor consultado (escapado como texto JSON) por <see cref="Con"/>.
/// </summary>
internal static class FixturesProveedor
{
    public const string Conductor = "proveedor-kyverum-conductor.json";
    public const string ConductorSinLicencias = "proveedor-kyverum-conductor-sin-licencias.json";
    public const string Rues = "proveedor-verifik-rues.json";
    public const string SimitPersona = "proveedor-verifik-simit-persona.json";
    public const string SimitVehiculo = "proveedor-verifik-simit-vehiculo.json";
    public const string SimitSinRegistros = "proveedor-verifik-simit-sin-registros.json";

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
