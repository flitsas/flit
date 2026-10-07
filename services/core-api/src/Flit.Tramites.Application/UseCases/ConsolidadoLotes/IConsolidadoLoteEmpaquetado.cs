using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D2/D4/D8, CF-10/CF-12/CF-19/CF-20/CF-22) — persistencia del carril de empaquetado
/// del lote de descarga masiva de consolidados: reclamo de partes con lease, cierre condicionado, fallo por parte,
/// fallo del lote y transición a estado terminal con la auditoría <c>lote_finalizado</c>.
/// <para><b>Orden de locks:</b> toda operación que escribe toma primero el lock del lote (<c>SELECT … FOR UPDATE</c>) y
/// después la parte o los ítems: el mismo orden que el reclamo de ítems, el cierre de ítems, el cierre del carril y la
/// cancelación (#13385), así nunca hay interbloqueo.</para>
/// <para><b>Intentos como testigo de exclusión:</b> el reclamo de una parte <c>pendiente</c> no toca <c>attempts</c>; el
/// re-reclamo de una parte <c>empaquetando</c> con el lease vencido suma 1. El cierre y el fallo exigen que
/// <c>attempts</c> siga siendo el que se reclamó: una ejecución anterior que despierte tarde no pisa a la nueva.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var reclamada = await empaquetado.ReclamarSiguienteParteAsync(settings.PartLeaseSeconds, ct);
/// var contenido = await empaquetado.LeerContenidoAsync(reclamada.Lote.Id, reclamada.Parte.PartNumber, ct);
/// // … ZIP + cifrado + subida …
/// var desenlace = await empaquetado.CerrarParteAsync(new CierreParteLote(…), ct);
/// if (desenlace == CierreParteDesenlace.Cerrada) await empaquetado.FinalizarLoteAsync(reclamada.Lote.Id, ct);
/// </code>
/// </remarks>
public interface IConsolidadoLoteEmpaquetado
{
    /// <summary>
    /// Reclama UNA parte en una sola sentencia: la del lote <c>en_proceso</c> o <c>empaquetando</c> más antiguo que
    /// tenga una parte <c>pendiente</c> (o <c>empaquetando</c> con el lease vencido), por <c>part_number</c>. La parte
    /// pasa a <c>empaquetando</c> con <c>lease_until = now() + leaseSegundos</c>. <c>FOR UPDATE SKIP LOCKED</c> lote → parte.
    /// </summary>
    /// <returns>El lote (con su DEK envuelta) y la parte reclamada, o <c>null</c> si no hay nada que empaquetar.</returns>
    Task<ParteLoteReclamada?> ReclamarSiguienteParteAsync(int leaseSegundos, CancellationToken ct = default);

    /// <summary>
    /// Contenido de la parte: los ítems <c>incluido</c> con ese <c>part_number</c> por <c>processed_at, position</c> (PDF) y
    /// los <c>omitido</c> por <c>position</c> (filas de <c>omitidos.csv</c>).
    /// </summary>
    Task<ContenidoParteLote> LeerContenidoAsync(Guid loteId, short partNumber, CancellationToken ct = default);

    /// <summary>
    /// AC6 — antes de subir: si el lote ya no admite cierres (cancelado, terminal o borrado), bajo el lock del lote deja
    /// la parte en <c>descartada</c> y devuelve <c>true</c> (no se sube nada). Si el lote sigue vivo devuelve
    /// <c>false</c> sin escribir.
    /// </summary>
    Task<bool> DescartarSiLoteInactivoAsync(Guid loteId, short partNumber, short intentos, CancellationToken ct = default);

    /// <summary>
    /// AC1/AC4/AC6 — en UNA transacción con el lock del lote:
    /// <list type="bullet">
    ///   <item>lote cancelado o terminal ⇒ la parte queda <c>descartada</c> (<see cref="CierreParteDesenlace.Descartada"/>);</item>
    ///   <item>si la parte sigue <c>empaquetando</c> con los mismos intentos ⇒ <c>cerrada</c> con
    ///   <c>plain_size_bytes</c>, <c>stored_size_bytes</c>, <c>stored_sha256</c>, <c>storage_path</c> y <c>closed_at</c>; los
    ///   ítems de <see cref="CierreParteLote.ItemsNoDisponibles"/> pasan de <c>incluido</c> a <c>omitido</c>
    ///   <c>adjunto_no_disponible</c> con los contadores de la parte y del lote ajustados;</item>
    ///   <item>si no ⇒ <see cref="CierreParteDesenlace.NoAplicada"/> sin escribir nada.</item>
    /// </list>
    /// </summary>
    Task<CierreParteDesenlace> CerrarParteAsync(CierreParteLote cierre, CancellationToken ct = default);

    /// <summary>
    /// Fallo técnico de una ejecución (excepción, timeout o intentos agotados por caídas). Con el lock del lote:
    /// <list type="bullet">
    ///   <item>lote cancelado o terminal ⇒ parte <c>descartada</c>;</item>
    ///   <item><see cref="FalloParteLote.Intentos"/> &lt; <see cref="FalloParteLote.MaxIntentos"/> ⇒ parte <c>pendiente</c> con
    ///   esos intentos (reintento en el ciclo siguiente);</item>
    ///   <item>si no ⇒ parte <c>fallida</c> y el lote pasa a <c>fallido</c> (AC3), igual que <see cref="FallarLoteAsync"/>.</item>
    /// </list>
    /// Solo actúa si la parte sigue <c>empaquetando</c> con los intentos de <see cref="FalloParteLote.IntentosReclamados"/>.
    /// </summary>
    Task<FalloParteDesenlace> RegistrarFalloParteAsync(FalloParteLote fallo, CancellationToken ct = default);

    /// <summary>
    /// AC3 — el lote pasa a <c>fallido</c> (p. ej. la DEK no se puede desenvolver) en UNA transacción con su lock:
    /// <c>finished_at</c>, <c>expires_at = finished_at + retention_hours</c>, <c>error_code</c>, DEK destruida (sin partes
    /// descargables), partes sin cerrar <c>descartada</c> (la que agotó intentos, <c>fallida</c>), ítems vivos
    /// <c>cancelado</c> y la auditoría <c>lote_finalizado</c>. El cupo «un lote activo por usuario» queda libre. Devuelve
    /// <c>false</c> si el lote ya no estaba activo.
    /// </summary>
    Task<bool> FallarLoteAsync(Guid loteId, string codigoError, CancellationToken ct = default);

    /// <summary>Lotes <c>empaquetando</c> sin partes por cerrar ni ítems vivos y sin ninguna parte <c>fallida</c>.</summary>
    Task<IReadOnlyList<Guid>> ObtenerLotesParaFinalizarAsync(int maximo, CancellationToken ct = default);

    /// <summary>
    /// AC2 — si el lote sigue <c>empaquetando</c>, todas sus partes están <c>cerrada</c> y no quedan ítems vivos, en UNA
    /// transacción con su lock: <c>completado</c> (0 omitidos) o <c>completado_con_omitidos</c>, <c>finished_at</c>,
    /// <c>expires_at = finished_at + retention_hours</c> y la auditoría <c>lote_finalizado</c>. <c>null</c> si no procede.
    /// </summary>
    Task<LoteFinalizado?> FinalizarLoteAsync(Guid loteId, CancellationToken ct = default);
}

/// <summary>Resultado de <see cref="IConsolidadoLoteEmpaquetado.ReclamarSiguienteParteAsync"/>.</summary>
/// <param name="Lote">Lote de la parte (incluye <c>dek_wrapped</c>).</param>
/// <param name="Parte">Parte en <c>empaquetando</c> con su lease y sus intentos tras el reclamo.</param>
public sealed record ParteLoteReclamada(ConsolidadoExportBatch Lote, ConsolidadoExportBatchPart Parte);

/// <summary>Contenido de una parte, ya ordenado.</summary>
public sealed record ContenidoParteLote(IReadOnlyList<PdfDeParte> Pdfs, IReadOnlyList<OmitidoDeParte> Omitidos);

/// <summary>Ítem <c>incluido</c> de una parte: el snapshot que se copia al ZIP.</summary>
public sealed record PdfDeParte(
    Guid ItemId,
    Guid TenantId,
    Guid ProcedureInstanceId,
    int Position,
    string ReferenceNumber,
    string? Plate,
    string StoragePath);

/// <summary>Ítem <c>omitido</c> de una parte: una fila del CSV.</summary>
public sealed record OmitidoDeParte(Guid ItemId, int Position, string ReferenceNumber, string? Plate, string? Motivo);

/// <summary>Datos del cierre de una parte.</summary>
/// <param name="LoteId">Lote.</param>
/// <param name="PartNumber">Parte.</param>
/// <param name="IntentosReclamados"><c>attempts</c> de la parte en el reclamo (testigo de exclusión).</param>
/// <param name="BytesEnClaro">Tamaño del ZIP en claro (<c>plain_size_bytes</c>, el <c>Content-Length</c> de la descarga).</param>
/// <param name="Almacenado">Objeto cifrado subido: <c>storage_path</c>, <c>stored_sha256</c> y <c>stored_size_bytes</c>.</param>
/// <param name="ItemsNoDisponibles">Incluidos cuyo PDF no se pudo leer ni del snapshot ni del adjunto actual (AC4).</param>
public sealed record CierreParteLote(
    Guid LoteId,
    short PartNumber,
    short IntentosReclamados,
    long BytesEnClaro,
    StoredFile Almacenado,
    IReadOnlyList<Guid> ItemsNoDisponibles);

/// <summary>Desenlace de <see cref="IConsolidadoLoteEmpaquetado.CerrarParteAsync"/>.</summary>
public enum CierreParteDesenlace
{
    /// <summary>La parte quedó <c>cerrada</c> con su binario.</summary>
    Cerrada,

    /// <summary>El lote ya no estaba vivo (cancelado o terminal): la parte quedó <c>descartada</c> (AC6).</summary>
    Descartada,

    /// <summary>La parte ya no estaba <c>empaquetando</c> con esos intentos (otra ejecución la retomó): nada escrito.</summary>
    NoAplicada,
}

/// <summary>Fallo técnico de una ejecución de empaquetado.</summary>
/// <param name="LoteId">Lote.</param>
/// <param name="PartNumber">Parte.</param>
/// <param name="IntentosReclamados"><c>attempts</c> de la parte en el reclamo (testigo de exclusión).</param>
/// <param name="Intentos">Intentos tras contar esta ejecución (normalmente <c>IntentosReclamados + 1</c>).</param>
/// <param name="MaxIntentos"><c>max_part_attempts</c>.</param>
public sealed record FalloParteLote(Guid LoteId, short PartNumber, short IntentosReclamados, short Intentos, short MaxIntentos);

/// <summary>Desenlace de <see cref="IConsolidadoLoteEmpaquetado.RegistrarFalloParteAsync"/>.</summary>
public enum FalloParteDesenlace
{
    /// <summary>La parte vuelve a <c>pendiente</c> para otro intento.</summary>
    Reprogramada,

    /// <summary>La parte agotó <c>max_part_attempts</c>: parte <c>fallida</c> y lote <c>fallido</c> (AC3).</summary>
    LoteFallido,

    /// <summary>El lote ya no estaba vivo: la parte quedó <c>descartada</c>.</summary>
    Descartada,

    /// <summary>La parte ya no estaba <c>empaquetando</c> con esos intentos: nada escrito.</summary>
    NoAplicada,
}

/// <summary>Resultado de <see cref="IConsolidadoLoteEmpaquetado.FinalizarLoteAsync"/>.</summary>
public sealed record LoteFinalizado(Guid LoteId, string Estado, DateTimeOffset FinishedAt, DateTimeOffset ExpiresAt);

/// <summary>Códigos de <c>consolidado_export_batches.error_code</c> del carril de empaquetado (sin datos personales).</summary>
public static class ConsolidadoLoteErrores
{
    /// <summary>Una parte agotó <c>max_part_attempts</c>.</summary>
    public const string ParteIntentosAgotados = "parte_intentos_agotados";

    /// <summary>La DEK envuelta del lote no se pudo desenvolver (llave de Data Protection ausente o valor alterado).</summary>
    public const string DekInvalida = "dek_invalida";
}
