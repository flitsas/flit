using System.Text;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13377 (Épica #13216, diseño §3, decisión Q6 = a, CF-09/CF-13) — <c>omitidos.csv</c> de una parte:
/// <list type="bullet">
///   <item>UTF-8 con BOM, separador <c>;</c> (Excel es-CO), fin de línea CRLF, encabezado
///   <see cref="Encabezado"/>.</item>
///   <item>Neutralización de fórmulas (CWE-1236): un valor que empieza por <c>=</c>, <c>+</c>, <c>-</c> o <c>@</c>
///   (y también tabulador o retorno de carro, OWASP) sale con un apóstrofo delante.</item>
///   <item>Un valor con <c>;</c>, comillas o saltos de línea va entre comillas dobles con las comillas duplicadas
///   (RFC 4180), para que no parta la fila.</item>
/// </list>
/// El escritor no ordena ni filtra: escribe las filas en el orden recibido. Nunca registra valores en logs.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var entrada = zip.CreateEntry(ConsolidadoLoteNombres.OmitidosCsv, CompressionLevel.Optimal).Open();
/// OmitidosCsvWriter.Escribir(entrada, [new OmitidoCsvFila("R-1", "ABC123", "Acceso revocado")]);
/// </code>
/// </remarks>
public static class OmitidosCsvWriter
{
    /// <summary>Separador de columnas.</summary>
    public const char Separador = ';';

    /// <summary>Primera línea del archivo.</summary>
    public const string Encabezado = "radicado;placa;motivo";

    /// <summary>Escribe el CSV completo (BOM + encabezado + filas) en <paramref name="destino"/>; no lo cierra.</summary>
    public static void Escribir(Stream destino, IEnumerable<OmitidoCsvFila> filas)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(filas);

        // BOM explícito: StreamWriter no lo escribe de forma fiable en un stream no buscable ya posicionado.
        destino.Write(Encoding.UTF8.Preamble);
        using var writer = new StreamWriter(destino, SinBom, bufferSize: 16 * 1024, leaveOpen: true) { NewLine = FinDeLinea };
        writer.WriteLine(Encabezado);
        foreach (var fila in filas)
        {
            ArgumentNullException.ThrowIfNull(fila);
            writer.Write(Celda(fila.Radicado));
            writer.Write(Separador);
            writer.Write(Celda(fila.Placa));
            writer.Write(Separador);
            writer.WriteLine(Celda(fila.Motivo));
        }

        writer.Flush();
    }

    /// <summary>El CSV completo en memoria (para partes pequeñas y pruebas).</summary>
    public static byte[] Generar(IEnumerable<OmitidoCsvFila> filas)
    {
        using var ms = new MemoryStream();
        Escribir(ms, filas);
        return ms.ToArray();
    }

    /// <summary>Valor de una celda: neutralizado contra fórmulas y entrecomillado si hace falta.</summary>
    public static string Celda(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
            return string.Empty;

        var neutro = InicioPeligroso.Contains(valor[0]) ? "'" + valor : valor;
        return neutro.AsSpan().IndexOfAny(ExigenComillas) >= 0
            ? "\"" + neutro.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : neutro;
    }

    private const string FinDeLinea = "\r\n";

    private static readonly UTF8Encoding SinBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Primer carácter que una hoja de cálculo interpreta como fórmula (AC5 + tabulador y CR de OWASP).</summary>
    private static readonly char[] InicioPeligroso = ['=', '+', '-', '@', '\t', '\r'];

    private static readonly System.Buffers.SearchValues<char> ExigenComillas =
        System.Buffers.SearchValues.Create([Separador, '"', '\r', '\n']);
}

/// <summary>Una fila de <c>omitidos.csv</c>.</summary>
/// <param name="Radicado">Radicado congelado del ítem.</param>
/// <param name="Placa">Placa congelada (puede faltar).</param>
/// <param name="Motivo">Texto legible del motivo (<c>omission_reason</c>).</param>
public sealed record OmitidoCsvFila(string? Radicado, string? Placa, string? Motivo);
