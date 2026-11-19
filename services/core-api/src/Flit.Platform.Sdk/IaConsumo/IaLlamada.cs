using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Flit.Platform.Sdk.IaConsumo;

/// <summary>
/// Lo que el cliente HTTP sabe de UN intento contra el proveedor. El registrador lo completa con el ámbito ambiental,
/// el usuario de la petición y la configuración del servicio. Nunca debe llevar el archivo ni su base64.
/// </summary>
public sealed record IaLlamada
{
    public required string Proveedor { get; init; }
    /// <summary>Tipo de tarea si el ámbito no dice otra cosa (visión → ocr, chat → chat).</summary>
    public required string TipoTareaPorDefecto { get; init; }
    public required DateTimeOffset FechaInicio { get; init; }
    /// <summary>null → el momento en que se registra.</summary>
    public DateTimeOffset? FechaFin { get; init; }
    public short Intento { get; init; } = 1;
    public required string Resultado { get; init; }
    /// <summary>0 = sin respuesta HTTP (timeout, red, sin llave).</summary>
    public int HttpStatus { get; init; }
    public string? ModeloSolicitado { get; init; }
    public string? ModeloRespondido { get; init; }
    public string? IdRespuestaProveedor { get; init; }
    public int? MaxTokens { get; init; }
    public string? StopReason { get; init; }
    public string? ErrorCodigo { get; init; }
    public string? ErrorMensaje { get; init; }
    public int TokensEntrada { get; init; }
    public int TokensSalida { get; init; }
    public int TokensCacheLectura { get; init; }
    public int TokensCacheEscritura { get; init; }
    /// <summary>Texto del prompt: se usa para <c>prompt_hash</c> y, con detalle activo, para <c>prompt_texto</c>.</summary>
    public string? PromptTexto { get; init; }
    /// <summary>
    /// Parte fija del prompt (p. ej. el <c>system</c> de un chat) para <c>prompt_hash</c>; null → se usa
    /// <see cref="PromptTexto"/>. Así el hash identifica la versión del prompt y no cambia con cada turno.
    /// </summary>
    public string? PromptBase { get; init; }
    /// <summary>Solo se guarda con <c>IA_CONSUMO_DETALLE=true</c>.</summary>
    public string? RespuestaTexto { get; init; }
    public string? ArchivoMime { get; init; }
    public long? ArchivoBytes { get; init; }
}

/// <summary>Utilidades del contrato: placa, recortes, hash.</summary>
public static partial class IaConsumoTexto
{
    /// <summary>Tope de <c>error_mensaje</c> (contrato regla 4).</summary>
    public const int MaxErrorMensaje = 500;

    /// <summary>Tope de <c>prompt_texto</c> y <c>respuesta_texto</c>: 64 KB cada uno (contrato regla 5).</summary>
    public const int MaxDetalle = 64 * 1024;

    /// <summary>MAYÚSCULAS, sin espacios ni guiones (contrato regla 8). Vacía → null.</summary>
    public static string? NormalizarPlaca(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa))
            return null;
        var sb = new StringBuilder(placa.Length);
        foreach (var c in placa)
        {
            if (char.IsWhiteSpace(c) || c == '-')
                continue;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// Quita del mensaje de error lo que puede ser un dato personal (contrato regla 4, criterio común con FLITO): correos →
    /// <c>[correo]</c> y secuencias de 6 o más dígitos (cédulas, NIT, teléfonos) → <c>[numero]</c>.
    /// </summary>
    public static string? SanearError(string? mensaje) =>
        mensaje is null ? null : Digitos().Replace(Correo().Replace(mensaje, "[correo]"), "[numero]");

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Correo();

    [GeneratedRegex(@"\d[\d.\s-]{4,}\d")]
    private static partial Regex Digitos();

    /// <summary>Recorta a <paramref name="max"/> caracteres sin partir un par sustituto.</summary>
    public static string? Recortar(string? texto, int max)
    {
        if (texto is null || texto.Length <= max)
            return texto;
        var corte = max;
        if (corte > 0 && char.IsHighSurrogate(texto[corte - 1]))
            corte--;
        return texto[..corte];
    }

    /// <summary>SHA-256 en hex minúscula del texto UTF-8 (<c>prompt_hash</c>).</summary>
    public static string? Sha256Hex(string? texto) =>
        texto is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    /// <summary>SHA-256 en hex minúscula de un binario (<c>archivo_sha256</c>).</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> contenido) => Convert.ToHexStringLower(SHA256.HashData(contenido));

    /// <summary>Bytes que representa un base64 sin decodificarlo.</summary>
    public static long BytesDeBase64(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
            return 0;
        var relleno = base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0;
        return (base64.Length / 4L * 3L) - relleno;
    }

    /// <summary>Ámbito con la descripción del archivo (sin el archivo): mime, tamaño y hash.</summary>
    public static IaAmbito DescribirArchivo(ReadOnlySpan<byte> contenido, string? mime, int? paginas = null) => new()
    {
        ArchivoMime = mime,
        ArchivoBytes = contenido.Length,
        ArchivoSha256 = Sha256Hex(contenido),
        ArchivoPaginas = paginas,
    };
}
