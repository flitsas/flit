using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #14469 — pago del portal ligado al trámite (<c>tramites.procedure_instance_portal_payments</c>, DDL 189). SQL
/// parametrizado (la tabla no es una entidad EF) y dentro de una transacción corta con el GUC de tenant para la política RLS.
/// </summary>
internal sealed partial class ProcedureInstanceRepository
{
    public Task<PagoPortalTramite?> GetPagoPortalAsync(Guid procedureInstanceId, Guid tenantId, CancellationToken ct) =>
        EnTenantAsync(tenantId, async () =>
        {
            var fila = await db.Database.SqlQuery<PagoPortalFila>($"""
                SELECT amount_cop, payment_method, cash_method, cash_method_detail, paid_at, charge_id
                FROM tramites.procedure_instance_portal_payments
                WHERE tenant_id = {tenantId} AND procedure_instance_id = {procedureInstanceId} AND deleted_at IS NULL
                LIMIT 1
                """).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return fila is null
                ? null
                : new PagoPortalTramite((long)fila.AmountCop, fila.PaymentMethod, fila.CashMethod, fila.CashMethodDetail, fila.PaidAt, fila.ChargeId);
        }, ct);

    public Task<bool> RegistrarPagoPortalAsync(Guid procedureInstanceId, Guid tenantId, PagoPortalTramite pago, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pago);
        return EnTenantAsync(tenantId, async () =>
        {
            var insertadas = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tramites.procedure_instance_portal_payments
                    (tenant_id, procedure_instance_id, amount_cop, payment_method, cash_method, cash_method_detail, paid_at, charge_id)
                VALUES ({tenantId}, {procedureInstanceId}, {pago.ValorCop}, {pago.Medio}, {pago.MedioCaja}, {pago.MedioDetalle}, {pago.PagadoEn}, {pago.CobroId})
                ON CONFLICT DO NOTHING
                """, ct).ConfigureAwait(false);
            return insertadas > 0;
        }, ct);
    }

    private async Task<T> EnTenantAsync<T>(Guid tenantId, Func<Task<T>> accion, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct).ConfigureAwait(false);
            return await accion().ConfigureAwait(false);
        }

        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct).ConfigureAwait(false);
            var resultado = await accion().ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return resultado;
        }).ConfigureAwait(false);
    }

    private sealed record PagoPortalFila(
        decimal AmountCop, string PaymentMethod, string? CashMethod, string? CashMethodDetail, DateTimeOffset PaidAt, string ChargeId);
}
