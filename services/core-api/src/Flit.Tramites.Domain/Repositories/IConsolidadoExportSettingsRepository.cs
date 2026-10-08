using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// HU #13420 (épica #13216) — lectura y edición, por el Super Admin, de la fila única de parámetros del motor de lotes
/// (<c>tramites.consolidado_export_settings</c>). La creación del lote y el motor siguen leyendo la fila en cada uso por
/// <see cref="IConsolidadoLoteRepository.ObtenerSettingsAsync"/> (sin caché), así que un cambio aplica sin reiniciar.
/// <list type="bullet">
///   <item>Concurrencia optimista por <c>row_version</c> (la sube el trigger <c>public.trg_row_version()</c>): si la
///   fila ya no tiene la versión leída, <see cref="ActualizarSettingsEstado.Conflicto"/> y no se escribe nada.</item>
///   <item>Auditoría: <c>updated_by</c>/<c>updated_at</c> en la fila y el trigger genérico <c>trg_audit_log</c> de la
///   tabla, que deja en <c>audit.audit_logs</c> la fila anterior y la nueva completas.</item>
///   <item>Nunca expone <c>PostgresException</c>: un CHECK violado se devuelve como
///   <see cref="ActualizarSettingsEstado.RechazadoPorLaBase"/> con el nombre de la restricción.</item>
/// </list>
/// <para>Uso de ejemplo:
/// <code>
/// var r = await repo.ActualizarAsync(valores, rowVersionLeido, usuarioId, ahora, ct);
/// if (r.Estado == ActualizarSettingsEstado.Conflicto) return Conflict();
/// </code></para>
/// </summary>
public interface IConsolidadoExportSettingsRepository
{
    /// <summary>La fila de parámetros con el nombre visible de quien la cambió por última vez; <c>null</c> sin fila.</summary>
    Task<ConsolidadoExportSettingsLeidos?> ObtenerAsync(CancellationToken ct = default);

    /// <summary>
    /// Escribe <paramref name="valores"/> (ya validados) si la fila conserva <paramref name="rowVersionLeido"/>, con
    /// <c>updated_by = usuarioId</c> y <c>updated_at = ahora</c>. Devuelve la fila releída tras guardar.
    /// </summary>
    Task<ActualizarSettingsResultado> ActualizarAsync(
        ConsolidadoExportSettingsValores valores,
        long rowVersionLeido,
        Guid? usuarioId,
        DateTimeOffset ahora,
        CancellationToken ct = default);
}

/// <summary>HU #13420 — fila de parámetros más el nombre visible del usuario de <c>updated_by</c> (si existe).</summary>
public sealed record ConsolidadoExportSettingsLeidos(ConsolidadoExportSettings Settings, string? ActualizadoPorNombre);

/// <summary>HU #13420 — desenlace de <see cref="IConsolidadoExportSettingsRepository.ActualizarAsync"/>.</summary>
public enum ActualizarSettingsEstado
{
    Actualizado,
    NoEncontrado,
    Conflicto,
    RechazadoPorLaBase,
}

/// <summary>HU #13420 — resultado de la escritura; <see cref="Leidos"/> solo en <see cref="ActualizarSettingsEstado.Actualizado"/>.</summary>
public sealed record ActualizarSettingsResultado(
    ActualizarSettingsEstado Estado,
    ConsolidadoExportSettingsLeidos? Leidos = null,
    string? Restriccion = null);
