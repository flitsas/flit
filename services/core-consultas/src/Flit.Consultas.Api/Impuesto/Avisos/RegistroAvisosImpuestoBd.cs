using Flit.Consultas.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Registro del receptor sobre el esquema propio (HU #14711), fuera de cualquier transacción: cada operación abre su scope, como
/// <see cref="SeguimientoImpuestoBd"/>. La deduplicación es un <c>INSERT … ON CONFLICT (tenant_id, delivery_id) DO NOTHING</c>
/// parametrizado sobre <c>consultas.vehicle_tax_webhook_deliveries</c>; el estado de la consulta sale de <c>vehicle_tax_jobs</c> filtrado
/// por la empresa del token (otra empresa ve «no encontrada»).
/// </summary>
/// <example>
/// <code>
/// if (await registro.RegistrarEntregaAsync(tenantId, delivery, idExterno, attempt, ct)) { /* primera vez */ }
/// var estado = await registro.EstadoConsultaAsync(tenantId, idExterno, ct);
/// </code>
/// </example>
internal sealed class RegistroAvisosImpuestoBd(IServiceScopeFactory scopes, TimeProvider time) : IRegistroAvisosImpuesto
{
    private static readonly string[] EstadosAbiertos = ["reserved", "queued", "running"];

    public async Task<bool> RegistrarEntregaAsync(Guid tenantId, string deliveryId, string idExterno, int attempt, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var insertadas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO consultas.vehicle_tax_webhook_deliveries (id, tenant_id, delivery_id, external_id, attempt, created_at)
            VALUES ({Guid.CreateVersion7()}, {tenantId}, {deliveryId}, {idExterno}, {attempt}, {time.GetUtcNow()})
            ON CONFLICT (tenant_id, delivery_id) DO NOTHING
            """, ct).ConfigureAwait(false);
        return insertadas == 1;
    }

    public async Task<EstadoConsultaAviso> EstadoConsultaAsync(Guid tenantId, string idExterno, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        var estados = await db.TrabajosImpuesto.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ExternalId == idExterno)
            .Select(t => t.Status)
            .Take(10)
            .ToListAsync(ct).ConfigureAwait(false);
        if (estados.Count == 0)
            return EstadoConsultaAviso.NoEncontrada;
        return estados.Any(e => EstadosAbiertos.Contains(e)) ? EstadoConsultaAviso.Abierta : EstadoConsultaAviso.Cerrada;
    }

    public async Task<int> PurgarAsync(DateTimeOffset antesDe, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsultasDb>();
        return await db.EntregasAvisoImpuesto.Where(e => e.CreatedAt < antesDe).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
