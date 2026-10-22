using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13420 (épica #13216) — <see cref="IConsolidadoExportSettingsRepository"/> sobre PostgreSQL.
/// <list type="bullet">
///   <item>Lectura sin tracking, siempre de la base (sin caché): la misma fila que leen el alta y el motor en cada
///   uso. El nombre visible sale de <c>identity.users.display_name</c> por <c>updated_by</c>, como el visor de
///   auditoría del admin; null si el usuario ya no existe.</item>
///   <item>Escritura: chequeo previo de <c>row_version</c> (camino rápido del 409) y, para la carrera entre la lectura y
///   el UPDATE, el token de concurrencia de EF (<c>WHERE row_version = @leida</c>): 0 filas ⇒
///   <see cref="DbUpdateConcurrencyException"/> ⇒ <see cref="ActualizarSettingsEstado.Conflicto"/>. El trigger
///   <c>tr_consolidado_export_settings_row_version</c> sube la versión y <c>tr_consolidado_export_settings_audit</c>
///   (<c>trg_audit_log</c>) deja en <c>audit.audit_logs</c> la fila anterior y la nueva.</item>
///   <item>Un CHECK violado (23514) sale como <see cref="ActualizarSettingsEstado.RechazadoPorLaBase"/> con el nombre de
///   la restricción; la <c>PostgresException</c> no sale de aquí.</item>
/// </list>
/// </summary>
internal sealed class ConsolidadoExportSettingsRepository(FlitDbContext db) : IConsolidadoExportSettingsRepository
{
    public async Task<ConsolidadoExportSettingsLeidos?> ObtenerAsync(CancellationToken ct = default)
    {
        var settings = await db.ConsolidadoExportSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (settings is null)
            return null;

        return new ConsolidadoExportSettingsLeidos(settings, await NombreAsync(settings.UpdatedBy, ct).ConfigureAwait(false));
    }

    public async Task<ActualizarSettingsResultado> ActualizarAsync(
        ConsolidadoExportSettingsValores valores,
        long rowVersionLeido,
        Guid? usuarioId,
        DateTimeOffset ahora,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(valores);

        var fila = await db.ConsolidadoExportSettings.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (fila is null)
            return new ActualizarSettingsResultado(ActualizarSettingsEstado.NoEncontrado);
        if (fila.RowVersion != rowVersionLeido)
            return new ActualizarSettingsResultado(ActualizarSettingsEstado.Conflicto);

        ConsolidadoExportSettingsRangos.Aplicar(fila, valores);
        fila.UpdatedAt = ahora;
        fila.UpdatedBy = usuarioId;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(fila).State = EntityState.Detached;
            return new ActualizarSettingsResultado(ActualizarSettingsEstado.Conflicto);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation } pg)
        {
            db.Entry(fila).State = EntityState.Detached;
            return new ActualizarSettingsResultado(ActualizarSettingsEstado.RechazadoPorLaBase, Restriccion: pg.ConstraintName);
        }

        // row_version la puso el trigger: se relee de la base (EF no la lee de vuelta).
        db.Entry(fila).State = EntityState.Detached;
        var leidos = await ObtenerAsync(ct).ConfigureAwait(false);
        return leidos is null
            ? new ActualizarSettingsResultado(ActualizarSettingsEstado.NoEncontrado)
            : new ActualizarSettingsResultado(ActualizarSettingsEstado.Actualizado, leidos);
    }

    private async Task<string?> NombreAsync(Guid? usuarioId, CancellationToken ct)
    {
        if (usuarioId is not { } id)
            return null;

        var nombre = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
    }
}
