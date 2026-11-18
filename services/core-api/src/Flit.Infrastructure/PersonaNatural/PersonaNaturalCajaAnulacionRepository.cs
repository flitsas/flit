using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// Ventana de anulación de la caja por organismo sobre <c>admin.persona_natural_caja_settings</c> y su historial
/// (<c>admin.persona_natural_caja_window_history</c>, DDL 200). SQL parametrizado: las tablas no son entidades EF. Sin caché.
/// </summary>
internal sealed class PersonaNaturalCajaAnulacionRepository(FlitDbContext db) : IPersonaNaturalCajaAnulacionRepository
{
    public async Task<int?> LeerAsync(Guid organismoId, CancellationToken ct = default)
    {
        var filas = await db.Database.SqlQuery<int>($"""
            SELECT annulment_window_minutes AS "Value" FROM admin.persona_natural_caja_settings WHERE transit_office_id = {organismoId}
            """).ToListAsync(ct).ConfigureAwait(false);
        return filas.Count == 0 ? null : filas[0];
    }

    public Task<int> GuardarAsync(Guid organismoId, int minutos, int minutosPorDefecto, Guid actor, CancellationToken ct = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                // La fila se bloquea para que dos cambios simultáneos no registren el mismo «antes».
                var previos = await db.Database.SqlQuery<int>($"""
                    SELECT annulment_window_minutes AS "Value" FROM admin.persona_natural_caja_settings
                     WHERE transit_office_id = {organismoId} FOR UPDATE
                    """).ToListAsync(ct).ConfigureAwait(false);
                var antes = previos.Count == 0 ? minutosPorDefecto : previos[0];

                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO admin.persona_natural_caja_settings (transit_office_id, annulment_window_minutes, created_by)
                    VALUES ({organismoId}, {minutos}, {actor})
                    ON CONFLICT (transit_office_id)
                    DO UPDATE SET annulment_window_minutes = EXCLUDED.annulment_window_minutes, updated_by = {actor}, updated_at = now()
                    """, ct).ConfigureAwait(false);
                if (antes != minutos)
                {
                    await db.Database.ExecuteSqlAsync($"""
                        INSERT INTO admin.persona_natural_caja_window_history (transit_office_id, minutes_before, minutes_after, changed_by)
                        VALUES ({organismoId}, {antes}, {minutos}, {actor})
                        """, ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return antes;
            }
        });

    public async Task<IReadOnlyList<CambioVentanaAnulacionView>> HistorialAsync(Guid organismoId, int max, CancellationToken ct = default)
    {
        var filas = await db.Database.SqlQuery<CambioFila>($"""
            SELECT h.minutes_before AS "MinutosAntes", h.minutes_after AS "MinutosDespues", u.display_name AS "ActorNombre", h.changed_at AS "CambiadoEn"
              FROM admin.persona_natural_caja_window_history h
              LEFT JOIN identity.users u ON u.id = h.changed_by
             WHERE h.transit_office_id = {organismoId}
             ORDER BY h.changed_at DESC, h.id DESC
             LIMIT {max}
            """).ToListAsync(ct).ConfigureAwait(false);
        return [.. filas.Select(f => new CambioVentanaAnulacionView(f.MinutosAntes, f.MinutosDespues, f.ActorNombre, f.CambiadoEn))];
    }

    private sealed class CambioFila
    {
        public int MinutosAntes { get; set; }
        public int MinutosDespues { get; set; }
        public string? ActorNombre { get; set; }
        public DateTimeOffset CambiadoEn { get; set; }
    }
}
