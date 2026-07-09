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
    /// <summary>
    /// Presupuesto de sondeos por ventana de actividad: solo se reclaman validaciones con
    /// <c>reconcile_poll_count &lt; MaxReconcilePolls</c>. Tras cada intento (o (re)envío) el presupuesto se
    /// reinicia a 0; así, si el cliente hace un intento y se queda quieto, el worker sondea a lo sumo N veces
    /// (~N·2 min) y luego calla en vez de pegarle a Kyverum cada 2 min durante horas.
    /// </summary>
    private const int MaxReconcilePolls = BiometricRules.KyverumMaxReconcilePolls;
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

        // Paso barato (SIN llamar a Kyverum): terminaliza una validación cuyo enlace de captura ya venció
        // (en_proceso + expires_at <= now) pasándola a `expirado`. Es la cura del "queda trabada": el registro
        // se limpia solo aunque el gestor nunca reabra el trámite, y desbloquea el reenvío (el guardia deja de
        // devolver biometria_activa). El worker de reconciliación normal EXCLUYE las vencidas (expires_at > now),
        // así que esta pasada es la única que las mueve fuera de en_proceso de forma determinista.
        var expiredId = await ClaimNextExpiredIdAsync(db, now, ct);
        if (expiredId is not null)
        {
            var expired = await db.ProcedureInstanceBiometricValidations.FirstAsync(x => x.Id == expiredId.Value, ct);
            expired.Status = BiometricEstados.Expirado;
            expired.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            ReconcileLog.Expired(logger, expired.Id);
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Expired, IdentityValidationAuditOutcomes.Expired,
                TenantId: expired.TenantId, ProcedureInstanceId: expired.ProcedureInstanceId, ValidationId: expired.Id,
                KyverumVerificationId: expired.KyverumVerificationId, PartyRole: expired.PartyRole,
                Message: "Worker: enlace de captura vencido; validación terminalizada como expirada.",
                Detail: $"expires_at={expired.ExpiresAt:O}"), ct);
            return true;
        }

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

        // Estampa updated_at siempre (aunque siga pendiente) para no re-sondear dentro de la ventana de frescura,
        // y consume una unidad del presupuesto de sondeo: al agotarlo el worker deja de reclamar esta validación
        // hasta que una señal nueva (intento por webhook, (re)envío o reconcile manual) lo reinicie a 0.
        if (v.Status is BiometricEstados.EnProceso or BiometricEstados.Enviado)
        {
            v.UpdatedAt = now;
            v.ReconcilePollCount += 1;
        }

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
                Message: updated ? "Worker: estado sincronizado por consulta." : "Worker: aún pendiente.",
                // Deja visible el validado_at que devolvió Kyverum + el presupuesto de sondeo restante:
                // así se diagnostica de un vistazo el jitter de validado_at y el corte del worker.
                Detail: $"validado_at={status.AttemptAt ?? "<null>"}; sondeo={v.ReconcilePollCount}/{MaxReconcilePolls}"), ct);
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
              AND reconcile_poll_count < @maxPolls
              AND COALESCE(updated_at, created_at) < @cutoff
            ORDER BY created_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;
        AddParam(cmd, "status", BiometricEstados.EnProceso);
        AddParam(cmd, "provider", BiometricProviders.Kyverum);
        AddParam(cmd, "maxPolls", MaxReconcilePolls);
        AddParam(cmd, "cutoff", cutoff);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }

    /// <summary>
    /// Reclama una validación Kyverum <c>en_proceso</c> cuyo enlace de captura ya VENCIÓ
    /// (<c>expires_at &lt;= @now</c>), con <c>FOR UPDATE SKIP LOCKED</c>. No consulta a Kyverum: solo se usa
    /// para terminalizarla como <c>expirado</c>. Devuelve null si no hay ninguna reclamable.
    /// </summary>
    private static async Task<Guid?> ClaimNextExpiredIdAsync(FlitDbContext db, DateTimeOffset now, CancellationToken ct)
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
              AND expires_at <= @now
            ORDER BY expires_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;
        AddParam(cmd, "status", BiometricEstados.EnProceso);
        AddParam(cmd, "provider", BiometricProviders.Kyverum);
        AddParam(cmd, "now", now);

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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reconciliación identidad: enlace vencido; validación {ValidationId} terminalizada como expirada.")]
    public static partial void Expired(ILogger logger, Guid validationId);

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
