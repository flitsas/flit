namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Firmas de archivo compartidas (brief C11, security-previa SP-17; HU #14576). <see cref="Pdf"/> la usa la carga para rechazar un
/// PDF disfrazado de planilla (<c>FormatoCarga</c>) y la evidencia del saneamiento para aceptarlo con <see cref="EsPdf"/>: <c>%PDF-</c>
/// exacto en el byte 0 y <c>%%EOF</c> en los últimos 1024 bytes, para no admitir políglotas (HTML o ZIP con cabecera PDF). No abre ni
/// interpreta el contenido.
/// </summary>
/// <example>
/// <code>
/// FirmasArchivo.EsPdf("%PDF-1.7\n...\n%%EOF\n"u8); // true
/// </code>
/// </example>
internal static class FirmasArchivo
{
    /// <summary>Ventana del final donde se busca <see cref="FinPdf"/>.</summary>
    public const int VentanaFin = 1024;

    /// <summary>Firma mínima de PDF (la que rechaza la carga).</summary>
    public static ReadOnlySpan<byte> Pdf => "%PDF"u8;

    /// <summary>Cabecera exacta de un PDF con versión.</summary>
    public static ReadOnlySpan<byte> CabeceraPdf => "%PDF-"u8;

    /// <summary>Marca de fin de un PDF.</summary>
    public static ReadOnlySpan<byte> FinPdf => "%%EOF"u8;

    /// <summary>True si empieza con <c>%PDF-</c> en el byte 0 y trae <c>%%EOF</c> en los últimos <see cref="VentanaFin"/> bytes.</summary>
    public static bool EsPdf(ReadOnlySpan<byte> contenido) =>
        contenido.StartsWith(CabeceraPdf)
        && contenido[Math.Max(0, contenido.Length - VentanaFin)..].IndexOf(FinPdf) >= 0;
}
