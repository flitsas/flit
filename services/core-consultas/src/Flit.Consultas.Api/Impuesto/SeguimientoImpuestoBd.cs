using Flit.Consultas.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>
/// Seguimiento fuera de la transacción del consumidor (H5): cada operación abre su propio scope/conexión.
/// </summary>
internal sealed class SeguimientoImpuestoBd(IServiceScopeFactory scopes, TimeProvider time) : ISeguimientoImpuesto
{
    private static readonly TimeSpan Retencion = TimeSpan.FromHours(24);

    public async Task<SeguimientoImpuesto?> LeerAsync(Guid trabajoId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var fila = await db.TrabajosImpuesto.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TrabajoId == trabajoId, ct).ConfigureAwait(false);
        return fila is null ? null : Mapear(fila);
    }

    public async Task<SeguimientoImpuesto> ReservarAsync(Guid trabajoId, Guid tenantId, string departamento, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var ahora = time.GetUtcNow();
        var corte = ahora - Retencion;
        await db.TrabajosImpuesto.Where(t => t.CreatedAt < corte).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO consultas.vehicle_tax_jobs
                (id, trabajo_id, tenant_id, department_code, external_id, status, submitted_at, last_polled_at, poll_count, completed_at, created_at, updated_at)
            VALUES
                ({Guid.CreateVersion7()}, {trabajoId}, {tenantId}, {departamento}, NULL, {"reserved"}, NULL, NULL, 0, NULL, {ahora}, {ahora})
            ON CONFLICT (trabajo_id) DO NOTHING
            """, ct).ConfigureAwait(false);

        var fila = await db.TrabajosImpuesto.AsNoTracking()
            .SingleAsync(t => t.TrabajoId == trabajoId, ct).ConfigureAwait(false);
        return Mapear(fila);
    }

    public async Task GuardarIdExternoAsync(Guid trabajoId, string idExterno, DateTimeOffset enviadoEn, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var fila = await db.TrabajosImpuesto.SingleAsync(t => t.TrabajoId == trabajoId, ct).ConfigureAwait(false);
        fila.ExternalId = idExterno;
        fila.SubmittedAt = enviadoEn;
        fila.Status = "queued";
        fila.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task OlvidarIdExternoAsync(Guid trabajoId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var fila = await db.TrabajosImpuesto.SingleAsync(t => t.TrabajoId == trabajoId, ct).ConfigureAwait(false);
        fila.ExternalId = null;
        fila.SubmittedAt = null;
        fila.Status = "reserved";
        fila.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CerrarAsync(Guid trabajoId, string estado, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var fila = await db.TrabajosImpuesto.SingleOrDefaultAsync(t => t.TrabajoId == trabajoId, ct).ConfigureAwait(false);
        if (fila is null)
            return;
        fila.Status = estado;
        fila.CompletedAt = time.GetUtcNow();
        fila.UpdatedAt = fila.CompletedAt.Value;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static SeguimientoImpuesto Mapear(TrabajoImpuestoExterno fila) =>
        new(fila.TrabajoId, fila.ExternalId, fila.SubmittedAt, fila.Status);
}
