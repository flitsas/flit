namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Cifrado de las partes ZIP de un lote de descarga masiva de consolidados (épica #13216, HU #13372, ADR-0070 §D4).
/// Cada lote tiene su propia clave de datos (DEK, 256 bits) que solo se persiste <b>envuelta</b> con ASP.NET
/// Data Protection en <c>consolidado_export_batches.dek_wrapped</c>. La purga pone esa columna en <c>NULL</c>: sin
/// la DEK el texto cifrado que quede en el almacenamiento es ilegible (borrado criptográfico).
/// <para>
/// La DEK en claro nunca sale de la implementación: este puerto recibe y devuelve solo la DEK envuelta. El
/// formato del archivo cifrado (<c>FLZ1</c>) está documentado en la implementación
/// (<c>Flit.Infrastructure.Security.ConsolidadoLoteCipher</c>).
/// </para>
/// </summary>
/// <example>
/// <code>
/// var dekWrapped = cipher.GenerarDekEnvuelta();            // al crear el lote
/// await cipher.CifrarAsync(dekWrapped, loteId, 1, zip, cifrado, ct);   // al empaquetar la parte 1
/// await cipher.DescifrarAsync(dekWrapped, loteId, 1, cifrado, respuesta, ct); // al descargarla
/// </code>
/// </example>
public interface IConsolidadoLoteCipher
{
    /// <summary>Genera una DEK aleatoria de 256 bits y la devuelve envuelta con Data Protection (valor de <c>dek_wrapped</c>).</summary>
    byte[] GenerarDekEnvuelta();

    /// <summary>
    /// Cifra en streaming <paramref name="origenEnClaro"/> hacia <paramref name="destinoCifrado"/> por bloques de 1 MiB.
    /// No cierra ninguno de los dos streams.
    /// </summary>
    /// <exception cref="ConsolidadoLoteCifradoException">DEK ausente o no desenvolvible.</exception>
    Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
        byte[]? dekEnvuelta,
        Guid loteId,
        int partNumber,
        Stream origenEnClaro,
        Stream destinoCifrado,
        CancellationToken ct = default);

    /// <summary>
    /// Descifra en streaming una parte. Solo escribe en <paramref name="destinoEnClaro"/> los bloques cuyo tag GCM
    /// se verificó: ante truncado, reordenado, otro lote u otra parte lanza
    /// <see cref="ConsolidadoLoteCifradoException"/> sin escribir el bloque afectado. Los bloques anteriores ya
    /// autenticados sí se escribieron: el consumidor HTTP debe fijar <c>Content-Length</c> para que el cliente detecte
    /// la respuesta incompleta. Devuelve los bytes en claro escritos.
    /// </summary>
    /// <exception cref="ConsolidadoLoteCifradoException">DEK ausente (<c>dek_wrapped = NULL</c>), no desenvolvible o parte corrupta.</exception>
    Task<long> DescifrarAsync(
        byte[]? dekEnvuelta,
        Guid loteId,
        int partNumber,
        Stream origenCifrado,
        Stream destinoEnClaro,
        CancellationToken ct = default);
}

/// <summary>Tamaños de una parte cifrada: el de claro se guarda para el <c>Content-Length</c> de la descarga.</summary>
public sealed record ConsolidadoLoteCifradoResultado(long BytesEnClaro, long BytesCifrados, long Bloques);

/// <summary>Motivo controlado de un fallo de cifrado/descifrado (sin detalle criptográfico ni datos del lote).</summary>
public enum ConsolidadoLoteCifradoError
{
    /// <summary>El lote no tiene DEK (<c>dek_wrapped = NULL</c>): purgado o cancelado.</summary>
    DekAusente,

    /// <summary>La DEK envuelta no se pudo desenvolver (llave de Data Protection revocada/ausente, o valor alterado).</summary>
    DekInvalida,

    /// <summary>El archivo no es un FLZ1 válido para este lote y parte: truncado, reordenado, alterado o ajeno.</summary>
    ParteCorrupta,
}

/// <summary>Error controlado del cifrado de partes de lote. El mensaje no incluye material de claves ni PII.</summary>
public sealed class ConsolidadoLoteCifradoException : Exception
{
    public ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError error, Exception? inner = null)
        : base(Mensaje(error), inner)
    {
        Error = error;
    }

    public ConsolidadoLoteCifradoError Error { get; }

    private static string Mensaje(ConsolidadoLoteCifradoError error) => error switch
    {
        ConsolidadoLoteCifradoError.DekAusente => "El lote no tiene clave de cifrado (purgado o cancelado).",
        ConsolidadoLoteCifradoError.DekInvalida => "La clave de cifrado del lote no se pudo desenvolver.",
        _ => "La parte cifrada del lote no es válida o fue alterada.",
    };
}
