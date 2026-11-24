namespace Flit.Consultas.Api.Simulacion;

/// <summary>Fixtures del impuesto asíncrono (<c>Simulacion/Fixtures/impuesto-v2/</c>), embebidos en el ensamblado.</summary>
internal static class FixturesImpuesto
{
    private static readonly string Prefijo = $"{typeof(FixturesImpuesto).Assembly.GetName().Name}.Simulacion.Fixtures.impuesto-v2.";

    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> Cargados = new(Cargar);

    public static IReadOnlyCollection<string> Nombres() => Cargados.Value.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public static string Texto(string archivo) => System.Text.Encoding.UTF8.GetString(Bytes(archivo));

    public static byte[] Bytes(string archivo) =>
        Cargados.Value.TryGetValue(archivo, out var bytes)
            ? bytes
            : throw new FileNotFoundException($"Fixture de impuesto no embebido: {archivo}");

    private static Dictionary<string, byte[]> Cargar()
    {
        var ensamblado = typeof(FixturesImpuesto).Assembly;
        var fixtures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var recurso in ensamblado.GetManifestResourceNames().Where(n => n.StartsWith(Prefijo, StringComparison.Ordinal)))
        {
            using var stream = ensamblado.GetManifestResourceStream(recurso)!;
            using var memoria = new MemoryStream();
            stream.CopyTo(memoria);
            fixtures[recurso[Prefijo.Length..]] = memoria.ToArray();
        }

        return fixtures;
    }
}
