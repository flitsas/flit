namespace Flit.Comparendos.Api.Infraestructura.Pdf;

/// <summary>
/// El membrete de un PDF de plantilla: una imagen de página completa (<see cref="Fondo"/>, el de Renting de V1, HU #13884) o las
/// bandas superior e inferior del membrete FLIT en SVG (Correos 2.0), copiadas de core-api
/// (<c>services/core-api/src/Flit.Infrastructure/Documents/Branding/Assets/membrete-hoja-{header,footer}.svg</c>) como recursos
/// embebidos en <c>Infraestructura/Pdf/Membretes/</c>, igual que en core-diagnostico.
/// </summary>
public sealed record MembretePdf(byte[]? Fondo, string? BandaSuperiorSvg, string? BandaInferiorSvg)
{
    private static readonly Lazy<MembretePdf> FlitCargado = new(() => new MembretePdf(null, Leer("membrete-hoja-header.svg"), Leer("membrete-hoja-footer.svg")));

    /// <summary>Las bandas del membrete FLIT.</summary>
    public static MembretePdf Flit => FlitCargado.Value;

    /// <summary>Un membrete de página completa en imagen.</summary>
    public static MembretePdf DeFondo(byte[] imagen) => new(imagen, null, null);

    private static string Leer(string archivo)
    {
        var asm = typeof(MembretePdf).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith($".Membretes.{archivo}", StringComparison.Ordinal)))!;
        using var lector = new StreamReader(stream);
        return lector.ReadToEnd();
    }
}
