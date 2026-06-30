using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Acceso de solo lectura + reencolado para observabilidad operativa de validación de identidad. Unifica
/// las DOS colas que pueden quedar en dead-letter: la outbox de completado
/// (<c>tramites.identity_validation_outbox</c>, encadenamiento firma/FUR) y la cola de ENVÍO al proveedor
/// (validaciones en <c>error_envio</c>). El despacho/reintento lo hacen los workers
/// (<c>IdentityValidationOutboxProcessor</c> / <c>IdentityValidationSendRetryProcessor</c>).
/// </summary>
internal sealed class IdentityValidationOutboxRepository(FlitDbContext db) : IIdentityValidationOutboxRepository
{
    /// <summary>EventType sintético para las filas de la cola de envío (la outbox usa el suyo real).</summary>
    private const string EnvioEventType = "identity_validation.send_failed";

    public async Task<IReadOnlyList<StuckIdentityValidationRow>> ListStuckAsync(
        Guid tenantId, int limit, CancellationToken ct = default)
    {
        // (1) Outbox de COMPLETADO atascada. Join (LEFT) con la validación biométrica por validation_id para
        // resolver el NOMBRE y el DOCUMENTO de la persona; el gestor necesita saber de quién es la validación
        // atascada. DefaultIfEmpty: si la validación ya no existe, los datos van null.
        var outboxQuery =
            from o in db.IdentityValidationOutbox.AsNoTracking()
            where o.TenantId == tenantId
                && o.PublishedAt == null
                && o.Attempts >= IdentityValidationOutbox.MaxDeliveryAttempts
            join v in db.ProcedureInstanceBiometricValidations.AsNoTracking()
                on o.ValidationId equals v.Id into vj
            from v in vj.DefaultIfEmpty()
            select new StuckIdentityValidationRow(
                o.Id,
                o.ValidationId,
                o.EventType,
                o.Attempts,
                o.OccurredAt,
                o.CreatedAt,
                v != null ? v.Name : null,
                v != null ? v.DocumentType : null,
                v != null ? v.DocumentNumber : null,
                StuckIdentityValidationKinds.Encadenamiento);

        // (2) Cola de ENVÍO al proveedor atascada (validaciones en error_envio). La persona sale directa de
        // la propia validación. El "id" de la fila es el de la validación (lo usa el requeue por id).
        var envioQuery =
            from v in db.ProcedureInstanceBiometricValidations.AsNoTracking()
            where v.TenantId == tenantId && v.Status == BiometricEstados.ErrorEnvio
            select new StuckIdentityValidationRow(
                v.Id,
                v.Id,
                EnvioEventType,
                v.Attempts,
                v.UpdatedAt ?? v.CreatedAt,
                v.CreatedAt,
                v.Name,
                v.DocumentType,
                v.DocumentNumber,
                StuckIdentityValidationKinds.Envio);

        // Dos consultas (no hay UNION tipado limpio con el LEFT JOIN); se materializan acotadas y se mezclan
        // por antigüedad. Vista de monitoreo acotada a `limit`, así que el merge en memoria es trivial.
        var outboxRows = await outboxQuery.Take(limit).ToListAsync(ct);
        var envioRows = await envioQuery.Take(limit).ToListAsync(ct);

        return outboxRows
            .Concat(envioRows)
            .OrderBy(r => r.OccurredAt)
            .Take(limit)
            .ToList();
    }

    public async Task<bool> RequeueStuckAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        // (1) ¿Es un evento de outbox dead-letter? Reinicia attempts → el worker lo vuelve a reclamar.
        var outboxRow = await db.IdentityValidationOutbox
            .FirstOrDefaultAsync(o => o.Id == id
                && o.TenantId == tenantId
                && o.PublishedAt == null
                && o.Attempts >= IdentityValidationOutbox.MaxDeliveryAttempts, ct);

        if (outboxRow is not null)
        {
            outboxRow.Attempts = 0;
            await db.SaveChangesAsync(ct);
            return true;
        }

        // (2) ¿Es una validación con el envío atascado (error_envio)? Vuelve a pendiente_envio con intentos a
        // 0 → el worker de envío la retoma. Ids de ambas tablas son Guids distintos: no hay colisión.
        var validation = await db.ProcedureInstanceBiometricValidations
            .FirstOrDefaultAsync(v => v.Id == id
                && v.TenantId == tenantId
                && v.Status == BiometricEstados.ErrorEnvio, ct);

        if (validation is null)
            return false;

        validation.Status = BiometricEstados.PendienteEnvio;
        validation.Attempts = 0;
        validation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> RequeueAllStuckAsync(Guid tenantId, CancellationToken ct = default)
    {
        // UPDATE en lote (sin materializar) de ambas colas: outbox dead-letter (attempts → 0) y validaciones
        // en error_envio (→ pendiente_envio, intentos → 0). Devuelve el total reencolado.
        var outboxRequeued = await db.IdentityValidationOutbox
            .Where(o => o.TenantId == tenantId
                && o.PublishedAt == null
                && o.Attempts >= IdentityValidationOutbox.MaxDeliveryAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Attempts, 0), ct);

        var envioRequeued = await db.ProcedureInstanceBiometricValidations
            .Where(v => v.TenantId == tenantId && v.Status == BiometricEstados.ErrorEnvio)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.Status, BiometricEstados.PendienteEnvio)
                .SetProperty(v => v.Attempts, 0)
                .SetProperty(v => v.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return outboxRequeued + envioRequeued;
    }
}
