using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4/D7/D8, CF-12/CF-22) — lectura del lote para su dueño, consulta de vencidos para
/// la purga a las 24 h y auditoría <c>parte_descargada</c>. Puerto propio (y no miembros nuevos de
/// <c>IConsolidadoLoteRepository</c>) para no tocar el ciclo «crear y retener» de #13373 ni sus dobles de prueba.
/// <list type="bullet">
///   <item><b>Dueño = <c>sub</c></b> (I1): toda lectura de un lote filtra por <c>requested_by_user_id</c>. Nunca por
///   tenant ni por <c>X-Tenant-Id</c>: un lote de otro usuario, incluido el de un Super Admin, no existe para el que
///   pregunta (404 en la API).</item>
///   <item>La purga en sí es <c>IConsolidadoLoteRepository.PurgarAsync</c> (misma operación de dominio que la creación).</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var lote = await lectura.ObtenerDelDuenoAsync(loteId, usuarioId, ct); // null ⇒ 404
/// var vencidos = await lectura.ObtenerVencidosAsync(DateTimeOffset.UtcNow, 100, ct);
/// </code>
/// </remarks>
public interface IConsolidadoLoteLectura
{
    /// <summary>El lote <paramref name="loteId"/> si su dueño es <paramref name="usuarioId"/> y no está borrado; si no, <c>null</c>.</summary>
    Task<ConsolidadoExportBatch?> ObtenerDelDuenoAsync(Guid loteId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// El lote activo del usuario o, si no tiene, el último lote terminal todavía retenido (<c>purged_at IS NULL</c>,
    /// incluido el que ya venció y espera la purga). <c>null</c> si no tiene ninguno (204 en la API).
    /// </summary>
    Task<ConsolidadoExportBatch?> ObtenerActualDelDuenoAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Partes del lote ordenadas por número.</summary>
    Task<IReadOnlyList<ConsolidadoExportBatchPart>> ObtenerPartesAsync(Guid loteId, CancellationToken ct = default);

    /// <summary>
    /// Lotes vencidos por purgar: terminales (incluido <c>fallido</c>), <c>expires_at &lt;= ahora</c>,
    /// <c>purged_at IS NULL</c>, sin borrado lógico; los que vencieron antes, primero. Usa
    /// <c>ix_consolidado_export_batches_purge</c>.
    /// </summary>
    Task<IReadOnlyList<Guid>> ObtenerVencidosAsync(DateTimeOffset ahora, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Inserta la fila <c>parte_descargada</c> (síncrona, antes del primer byte). <c>false</c> si no se pudo registrar:
    /// el llamador responde 503 sin bytes. Nunca lanza salvo cancelación.
    /// </summary>
    Task<bool> RegistrarDescargaAsync(ParteDescargadaRegistro registro, CancellationToken ct = default);
}

/// <summary>Datos de la fila <c>parte_descargada</c>. Sin PII más allá de IP y navegador (<c>@pii:medium/low</c>).</summary>
/// <param name="Lote">Lote del dueño (origen, compañía y tipo se copian de él).</param>
/// <param name="PartNumber">Parte descargada.</param>
/// <param name="RolCodigo">Rol con el que se pide la descarga.</param>
/// <param name="OcurridoEn">Instante de la descarga.</param>
/// <param name="ClientIp">IP del cliente.</param>
/// <param name="UserAgent">Navegador (ya truncado).</param>
public sealed record ParteDescargadaRegistro(
    ConsolidadoExportBatch Lote,
    short PartNumber,
    string RolCodigo,
    DateTimeOffset OcurridoEn,
    IPAddress? ClientIp,
    string? UserAgent);

/// <summary>Resultado de <see cref="ConsolidadoLoteDescargabilidad.Evaluar"/>.</summary>
public enum ConsolidadoLoteDescargable
{
    /// <summary><c>completado</c> o <c>completado_con_omitidos</c>, con DEK, sin purgar y con <c>expires_at &gt; ahora</c>.</summary>
    Si,

    /// <summary>El lote sigue activo (409 <c>lote_no_terminado</c>).</summary>
    NoTerminado,

    /// <summary>Purgado, vencido o sin DEK (410 <c>descarga_expirada</c>).</summary>
    Expirado,

    /// <summary><c>fallido</c> o <c>cancelado</c>: nunca tuvo partes descargables (404).</summary>
    SinPartes,
}

/// <summary>
/// HU #13379 — regla única de «parte descargable» (la usan la consulta, para listar partes, y la descarga). Un lote
/// <c>fallido</c> nunca es descargable: su DEK es <c>NULL</c> (#13378).
/// </summary>
/// <remarks>Uso de ejemplo: <c>ConsolidadoLoteDescargabilidad.Evaluar(lote, ahora) == ConsolidadoLoteDescargable.Si</c>.</remarks>
public static class ConsolidadoLoteDescargabilidad
{
    public static ConsolidadoLoteDescargable Evaluar(ConsolidadoExportBatch lote, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (ConsolidadoExportStatus.EsActivo(lote.Status))
            return ConsolidadoLoteDescargable.NoTerminado;
        if (lote.Status is ConsolidadoExportStatus.Fallido or ConsolidadoExportStatus.Cancelado)
            return ConsolidadoLoteDescargable.SinPartes;
        if (lote.Status is not (ConsolidadoExportStatus.Completado or ConsolidadoExportStatus.CompletadoConOmitidos)
            || lote.PurgedAt is not null
            || lote.DekWrapped is null
            || lote.ExpiresAt is not { } expira
            || expira <= ahora)
            return ConsolidadoLoteDescargable.Expirado;
        return ConsolidadoLoteDescargable.Si;
    }
}
