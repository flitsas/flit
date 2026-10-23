using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Contador diario de DR. FLIT sobre <c>dr_flit.daily_message_usage</c> (HU #12919). Sin entidad EF: el
/// consumo es un único <c>INSERT … ON CONFLICT DO UPDATE … WHERE message_count &lt; tope</c>, que Postgres
/// resuelve atómico por la unicidad (tenant, usuario, día). Así dos mensajes simultáneos no pueden dejar
/// el contador por encima del tope, cosa que un leer-comparar-escribir desde EF sí permitiría.
/// </summary>
internal sealed class DrFlitUsageCounterRepository(FlitDbContext db, Func<DateOnly>? today = null) : IDrFlitUsageCounter
{
    // Inyectable solo para las pruebas del cambio de día; en producción, siempre el día Colombia.
    private readonly Func<DateOnly> _today = today ?? BogotaDays.Today;

    public async Task<int> GetUsedTodayAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var day = _today();
        var rows = await db.Database
            .SqlQuery<int>($"""
                SELECT message_count AS "Value"
                  FROM dr_flit.daily_message_usage
                 WHERE tenant_id = {tenantId} AND user_id = {userId} AND usage_date = {day}
                """)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : 0;
    }

    public async Task<DrFlitUsageConsumption> TryConsumeAsync(
        Guid tenantId, Guid userId, int dailyLimit, CancellationToken ct)
    {
        // Un tope en 0 (o negativo por error de configuración) apaga el chat para todos sin tocar la tabla.
        if (dailyLimit <= 0)
            return new DrFlitUsageConsumption(false, await GetUsedTodayAsync(tenantId, userId, ct).ConfigureAwait(false));

        var day = _today();
        // El WHERE del DO UPDATE hace que, con el tope alcanzado, la sentencia no toque la fila y no
        // devuelva nada: esa es la señal de rate limit.
        var rows = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO dr_flit.daily_message_usage (tenant_id, user_id, usage_date, message_count, created_by, updated_by)
                VALUES ({tenantId}, {userId}, {day}, 1, {userId}, {userId})
                ON CONFLICT (tenant_id, user_id, usage_date) DO UPDATE
                   SET message_count = dr_flit.daily_message_usage.message_count + 1,
                       updated_at = now(),
                       updated_by = EXCLUDED.updated_by
                 WHERE dr_flit.daily_message_usage.message_count < {dailyLimit}
                RETURNING message_count AS "Value"
                """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count > 0)
            return new DrFlitUsageConsumption(true, rows[0]);

        // Tope alcanzado. Si lo bajaron durante el día, lo usado puede quedar por encima del tope nuevo:
        // se reporta el tope, que es lo que el usuario puede entender ("usaste 30 de 30").
        var used = await GetUsedTodayAsync(tenantId, userId, ct).ConfigureAwait(false);
        return new DrFlitUsageConsumption(false, Math.Min(used, dailyLimit));
    }
}
