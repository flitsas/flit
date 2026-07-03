using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Red de seguridad para validaciones de identidad COLGADAS: sondea periódicamente las validaciones Kyverum
/// en <c>en_proceso</c> que llevan rato sin actualizarse (no expiradas) y consulta su estado real en Kyverum
/// (<see cref="IKyverumVerifyClient.GetStatusAsync"/>). Si Kyverum ya la resolvió (aprobado/rechazado/expirado)
/// aplica el resultado con la MISMA lógica del webhook (<see cref="IdentityValidationResultApplier"/>). Cubre
/// el caso de webhook perdido (p.ej. callback mal ruteado entre ambientes) SIN que nadie tenga que abrir el
/// trámite. Reclama cada fila con <c>FOR UPDATE SKIP LOCKED</c> (seguro con varias réplicas) y estampa
/// <c>updated_at</c> en cada sondeo para no repetir la consulta dentro de la ventana de frescura (rate-limit).
/// </summary>
internal sealed class IdentityValidationReconcileProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityValidationReconcileProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    /// <summary>No se sondea una validación tocada hace menos de esto (deja espacio al webhook y al poll del front).</summary>
    private static readonly TimeSpan Staleness = TimeSpan.FromSeconds(120);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                for (var i = 0; i < BatchSize; i++)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;
                    if (!await ProcessNextClaimedAsync(stoppingToken))
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ReconcileLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<bool> ProcessNextClaimedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var kyverum = scope.ServiceProvider.GetRequiredService<IKyverumVerifyClient>();
        var applier = scope.ServiceProvider.GetRequiredService<IdentityValidationResultApplier>();
        var audit = scope.ServiceProvider.GetRequiredService<IIdentityValidationAuditLog>();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => await ProcessOneAsync(db, kyverum, applier, audit, ct));
    }

    private async Task<bool> ProcessOneAsync(
        FlitDbContext db, IKyverumVerifyClient kyverum, IdentityValidationResultApplier applier,
        IIdentityValidationAuditLog audit, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var claimedId = await ClaimNextIdAsync(db, now - Staleness, ct);
        if (claimedId is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var v = await db.ProcedureInstanceBiometricValidations.FirstAsync(x => x.Id == claimedId.Value, ct);

        KyverumVerifyStatus? status = null;
        var updated = false;
        try
        {
            status = await kyverum.GetStatusAsync(v.KyverumVerificationId!, v.PartyRole, ct);
            if (status is not null)
                updated = await IdentityValidationReconciler.ApplyStatusAsync(applier, v, status, now, ct);
        }
        catch (KyverumVerifyException ex)
        {
            // Transitorio/definitivo: no se bloquea la cola; se reintenta en el próximo ciclo tras la ventana.
            ReconcileLog.PollFailed(logger, v.Id, ex.Transient, ex);
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ErrorType: nameof(KyverumVerifyException),
                Message: "Worker: falló la consulta de estado; se reintenta."), ct);
        }

        // Estampa updated_at siempre (aunque siga pendiente) para no re-sondear dentro de la ventana de frescura.
        if (v.Status is BiometricEstados.EnProceso or BiometricEstados.Enviado)
            v.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La PERSISTENCIA falló (p.ej. DbUpdateException). Antes esto solo iba al stdout del pod y la
            // bitácora quedaba con un "aprobado" fantasma. Ahora el error REAL queda en la bitácora.
            try { await tx.RollbackAsync(ct); } catch { /* la tx ya pudo quedar abortada */ }
            ReconcileLog.PersistFailed(logger, v.Id, ex);
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ErrorType: ex.GetType().Name, HttpStatus: 500,
                Message: "Worker: falló al guardar el resultado. " + Truncate(ex.GetBaseException().Message)), ct);
            return true;
        }

        // Solo DESPUÉS de persistir OK se registra el resultado real (evita el "aprobado" que no se guardó).
        if (status is not null)
        {
            if (updated)
                ReconcileLog.Reconciled(logger, v.Id, v.Status);
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile,
                updated ? v.Status : IdentityValidationAuditOutcomes.Pending,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ProviderStatus: status.Status,
                Message: updated ? "Worker: estado sincronizado por consulta." : "Worker: aún pendiente."), ct);
        }
        return true;
    }

    /// <summary>Recorta el mensaje de error para la bitácora (evita textos enormes de EF/Npgsql).</summary>
    private static string Truncate(string message) =>
        string.IsNullOrEmpty(message) || message.Length <= 300 ? message : message[..300];

    /// <summary>
    /// Reclama la validación Kyverum <c>en_proceso</c> más antigua no expirada cuyo último toque
    /// (<c>updated_at</c> o <c>created_at</c>) sea anterior a <paramref name="cutoff"/>, con
    /// <c>FOR UPDATE SKIP LOCKED</c>. Devuelve null si no hay ninguna reclamable.
    /// </summary>
    private static async Task<Guid?> ClaimNextIdAsync(FlitDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT id
            FROM tramites.procedure_instance_biometric_validations
            WHERE status = @status
              AND provider = @provider
              AND kyverum_verification_id IS NOT NULL
              AND expires_at > now()
              AND COALESCE(updated_at, created_at) < @cutoff
            ORDER BY created_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;
        AddParam(cmd, "status", BiometricEstados.EnProceso);
        AddParam(cmd, "provider", BiometricProviders.Kyverum);
        AddParam(cmd, "cutoff", cutoff);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

/// <summary>Logging source-generated (CA1848) del worker de reconciliación.</summary>
internal static partial class ReconcileLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reconciliación identidad: validación {ValidationId} sincronizada con Kyverum → {Estado}.")]
    public static partial void Reconciled(ILogger logger, Guid validationId, string estado);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Reconciliación identidad: falló la consulta de {ValidationId} (transitorio={Transient}); se reintentará.")]
    public static partial void PollFailed(ILogger logger, Guid validationId, bool transient, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Reconciliación identidad: falló al guardar el resultado de {ValidationId}; se reintentará.")]
    public static partial void PersistFailed(ILogger logger, Guid validationId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Reconciliación identidad: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
