using System.Globalization;
using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia EF de pre-trámites con aislamiento por tenant (RLS). Cada unidad de trabajo abre una
/// transacción y fija el GUC <c>app.current_tenant_id</c> (local a la transacción). Como el DbContext
/// tiene EnableRetryOnFailure, la transacción va DENTRO de la execution strategy de EF (requisito para
/// combinar reintentos + transacciones iniciadas por el usuario).
/// </summary>
public sealed class PreTramiteRepository(IctDbContext db, IOptions<IctIngestOptions> ingestOptions)
    : IPreTramiteRepository
{
    // Lever B (default OFF): relajar la durabilidad del commit SOLO en el camino de ingesta del registro.
    private readonly bool _relaxRegisterDurability = ingestOptions.Value.RelaxRegisterCommitDurability;

    public Task<Guid> AddAsync(ExternalIntegrationMaster master, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        return InTenantTransactionAsync(tenantId, async () =>
        {
            db.Masters.Add(master);
            await db.SaveChangesAsync(ct);

            // Primer estado del histórico = Registrado (1), como en v1 (statusProcess[]). El registrador
            // colapsa sub-pasos por estado; la fila Registrado de v1 va con rol vacío (observation='' rol='').
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_process_status({master.Id}, {tenantId}, 1, 'REGISTRADO',
                    {master.ManagerUser}, {master.ManagerMail}, {master.CompanyManagerDocument}, '', '')
                """, ct);

            // Timeline de negocio: pre-trámite recibido (detail por allowlist, sin PII).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({master.Id}, {tenantId}, 'recibido', 'ok',
                    jsonb_build_object('transaction_type', {master.TransactionType},
                                       'manager_id_transaction', {master.ManagerIdTransaction},
                                       'traffic_secretary_code', {master.TrafficSecretaryCode}))
                """, ct);
            return master.Id;
        }, ct, relaxedDurability: _relaxRegisterDurability);
    }

    public Task<ExternalIntegrationMaster?> GetAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        InTenantTransactionAsync(
            tenantId,
            () => db.Masters.Include(m => m.Actors).FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct),
            ct);

    public Task<ExternalIntegrationMaster?> FindByManagerIdTransactionAsync(
        string reference,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // La referencia pública puede ser el número secuencial que devuelve /register (transaction_number,
        // paridad v1) o el manager_id_transaction propio del gestor. Se prioriza el número (llave asignada
        // por FLIT): si la referencia es numérica y hay match por número, se usa ese; si no, se cae al
        // manager_id_transaction. Determinístico y sin ambigüedad de colisión número↔ref.
        var number = long.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : (long?)null;
        return InTenantTransactionAsync(
            tenantId,
            async () =>
            {
                if (number is not null)
                {
                    var byNumber = await db.Masters
                        .Include(m => m.Actors)
                        .FirstOrDefaultAsync(m => m.TransactionNumber == number && m.DeletedAt == null, ct);
                    if (byNumber is not null)
                    {
                        return byNumber;
                    }
                }

                return await db.Masters
                    .Include(m => m.Actors)
                    .FirstOrDefaultAsync(m => m.ManagerIdTransaction == reference && m.DeletedAt == null, ct);
            },
            ct);
    }

    public async Task SaveAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            await InTenantTransactionAsync(tenantId, async () =>
            {
                await db.SaveChangesAsync(ct);
                return 0;
            }, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new IctConcurrencyException("Conflicto de row_version al editar el pre-trámite.", ex);
        }
    }

    public Task MarkAbortedAsync(
        Guid masterId,
        Guid tenantId,
        string observation,
        string user,
        string mail,
        string company,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(tenantId, async () =>
        {
            // El trámite ya fue anulado en core-api (autoridad). Aquí solo se refleja en el pre-trámite
            // y su histórico para que el endpoint de estado v1 muestre 'ANULADO' (6).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ict.external_integration_master
                SET process_status_id = 6, updated_at = now()
                WHERE id = {masterId} AND tenant_id = {tenantId};
                SELECT ict.record_process_status({masterId}, {tenantId}, 6, 'ANULADO',
                    {user}, {mail}, {company}, {observation}, 'integration')
                """, ct);

            // Timeline: anulado. NO se incluye la observation (texto libre del cliente → riesgo PII).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({masterId}, {tenantId}, 'anulado', 'ok')
                """, ct);
            return 0;
        }, ct);

    public Task EnqueueAbortWebhookAsync(
        Guid masterId,
        Guid tenantId,
        string observation,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(tenantId, async () =>
        {
            // Solo para pre-trámites NO materializados: para los materializados el webhook lo emite core-api
            // por el Plano C (IctStateCallbackService encola la fila al recibir el callback de 'anulado').
            // Aquí NO hay trámite en core-api, así que se encola directo, en la MISMA forma que ese callback
            // (INSERT ... SELECT del master para tomar manager_id_transaction/transaction_type/url_web_hook).
            // La observation vuelve al mismo gestor que la envió → sí viaja como mensaje (a diferencia del
            // timeline). status_validation=6 (Anulado); el Job 5 la entrega con vocabulario v2.
            var message = string.IsNullOrWhiteSpace(observation)
                ? "Trámite anulado por el gestor"
                : "ANULADO: " + observation;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ict.external_integration_webhook_master
                    (id_transaction, tenant_id, manager_id_transaction, transaction_type, status_validation,
                     message_validation, ict_estado, target_url)
                SELECT m.id, m.tenant_id, m.manager_id_transaction, m.transaction_type, 6,
                       {message}, 'anulado', m.url_web_hook
                FROM ict.external_integration_master m
                WHERE m.id = {masterId} AND m.tenant_id = {tenantId}
                """, ct);
            return 0;
        }, ct);

    public Task RecordTimelineEventAsync(
        Guid masterId,
        Guid tenantId,
        string stage,
        string outcome,
        string? detailJson,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(tenantId, async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({masterId}, {tenantId}, {stage}, {outcome}, {detailJson}::jsonb)
                """, ct);
            return 0;
        }, ct);

    /// <summary>Ejecuta <paramref name="work"/> en una transacción con el GUC de tenant fijado, dentro de la execution strategy.</summary>
    private async Task<T> InTenantTransactionAsync<T>(
        Guid tenantId, Func<Task<T>> work, CancellationToken ct, bool relaxedDurability = false)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct);
            // Lever B (opcional, default OFF vía IctIngestOptions.RelaxRegisterCommitDurability): en el camino
            // de INGESTA del registro se relaja la durabilidad del commit (synchronous_commit LOCAL a ESTA
            // transacción) para no esperar el fsync del WAL en cada fila (el registro hace 1 commit por fila).
            // El pre-trámite es staging REPROCESABLE: perder los últimos ms de commits ante un crash del
            // servidor es tolerable (el gestor reintenta/reconcilia). SET LOCAL ⇒ solo esta tx, se revierte al
            // commit (no contamina la conexión pooled). NO se aplica a reads ni a abort/edit/timeline.
            if (relaxedDurability)
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL synchronous_commit = off", ct);
            }
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
    }
}
