namespace Flit.Consultas.Api.Simulacion;

/// <summary>
/// Respuestas inventadas del RUNT por Intempo, versionadas como recursos del ensamblado (<c>Simulacion/Fixtures/intempo-*.json</c>):
/// proto-JSON de <c>ConsultarVehiculoIntempoResponse</c> con datos ficticios (rango TST / 99999…). Son las mismas que usaba la fuente
/// simulada de core-diagnostico (HU #13725, #13877), movidas aquí por ADR-0075 (U3 = a). Nunca se construyen a partir de una muestra
/// real.
/// </summary>
internal static class FixturesIntempo
{
    private static readonly string Prefijo = $"{typeof(FixturesIntempo).Assembly.GetName().Name}.Simulacion.Fixtures.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Cargados = new(Cargar);

    /// <summary>Nombre del archivo → contenido JSON.</summary>
    public static IReadOnlyDictionary<string, string> Todos() => Cargados.Value;

    private static Dictionary<string, string> Cargar()
    {
        var ensamblado = typeof(FixturesIntempo).Assembly;
        var fixtures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recurso in ensamblado.GetManifestResourceNames().Where(n => n.StartsWith(Prefijo + "intempo-", StringComparison.Ordinal)))
        {
            using var stream = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(stream);
            fixtures[recurso[Prefijo.Length..]] = lector.ReadToEnd();
        }

        return fixtures;
    }
}
