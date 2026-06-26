using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Worker de la COLA DE ENVÍO de validaciones de identidad (provider-agnostic). Sondea las validaciones en
/// <c>pendiente_envio</c> (encoladas por el handler cuando el envío al proveedor falló de forma
/// transitoria) y REINTENTA el envío resolviendo el proveedor por nombre vía
/// <see cref="IIdentityValidationProviderResolver"/> — así funciona para Kyverum o cualquier otro proveedor
/// sin tocar este worker. Al lograr el envío pasa la validación a <c>en_proceso</c> (con captureUrl,
/// verification_id y el secreto del webhook cifrado) y emite <see cref="IdentityValidationRequested"/>;
/// si agota los intentos (o el fallo es definitivo) la deja en <c>error_envio</c> (dead-letter observable).
/// Reclama cada fila con <c>FOR UPDATE SKIP LOCKED</c> → seguro con varias réplicas de core-api.
/// </summary>
internal sealed class IdentityValidationSendRetryProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityValidationSendRetryProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(7);
    private const int BatchSize = 20;

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
                SendRetryLog.CycleError(logger, ex);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Reclama UNA validación <c>pendiente_envio</c> (con intentos por debajo del tope) con
    /// <c>FOR UPDATE SKIP LOCKED</c> y reintenta el envío. Devuelve false si no hay ninguna reclamable.
    /// </summary>
    private async Task<bool> ProcessNextClaimedAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<IIdentityValidationProviderResolver>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>();
        var events = scope.ServiceProvider.GetRequiredService<IIdentityValidationEventPublisher>();

        // El DbContext usa EnableRetryOnFailure → las transacciones manuales deben ejecutarse dentro de la
        // execution strategy (que reintenta el bloque completo ante fallos transitorios de BD).
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
            await ProcessOneAsync(db, resolver, secretProtector, events, ct));
    }

    private async Task<bool> ProcessOneAsync(
        FlitDbContext db,
        IIdentityValidationProviderResolver resolver,
        IWebhookSecretProtector secretProtector,
        IIdentityValidationEventPublisher events,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // estado limpio en cada (re)intento de la strategy
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimedId = await ClaimNextIdAsync(db, ct);
        if (claimedId is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var v = await db.ProcedureInstanceBiometricValidations.FirstAsync(x => x.Id == claimedId.Value, ct);
        var now = DateTimeOffset.UtcNow;

        var provider = resolver.Resolve(v.Provider);
        if (provider is null)
        {
            // Proveedor no registrado (mala config / proveedor retirado): no se puede reintentar.
            v.Estado = BiometricEstados.ErrorEnvio;
            v.UpdatedAt = now;
            SendRetryLog.NoProvider(logger, v.Id, v.Provider);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        try
        {
            var result = await provider.StartAsync(
                new IdentityProviderStartRequest(
                    v.ProcedureInstanceId, v.Id, v.Parte, v.Nombre, v.TipoDoc, v.Documento, v.Email),
                ct);

            // Envío logrado → en_proceso con los datos del proveedor.
            v.Estado = BiometricEstados.EnProceso;
            v.KyverumVerificationId = result.VerificationId;
            v.CaptureUrl = result.CaptureUrl;
            v.WebhookSecretEncrypted = string.IsNullOrEmpty(result.WebhookSecret)
                ? null
                : secretProtector.Protect(result.WebhookSecret);
            v.ProviderStatus = result.ProviderStatus;
            v.ProviderPayload = result.RawPayloadSanitized;
            v.UpdatedAt = now;

            await events.PublishAsync(new IdentityValidationRequested
            {
                TenantId = v.TenantId,
                ProcedureInstanceId = v.ProcedureInstanceId,
                ValidationId = v.Id,
                Provider = v.Provider,
                Parte = v.Parte,
                ProviderVerificationId = result.VerificationId,
            }, ct);

            SendRetryLog.Sent(logger, v.Id, v.Provider, v.Intentos);
        }
        catch (IdentityProviderException ex)
        {
            v.Intentos += 1;
            v.UpdatedAt = now;
            // Definitivo, o agotó intentos → dead-letter (error_envio). Transitorio con intentos restantes
            // → sigue en pendiente_envio y se reintenta en el próximo ciclo.
            if (!ex.Transient || v.Intentos >= v.MaxIntentos)
            {
                v.Estado = BiometricEstados.ErrorEnvio;
                SendRetryLog.DeadLettered(logger, v.Id, v.Provider, v.Intentos, ex.Transient);
            }
            else
            {
                SendRetryLog.RetryScheduled(logger, v.Id, v.Provider, v.Intentos, ex);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static async Task<Guid?> ClaimNextIdAsync(FlitDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var transaction = db.Database.CurrentTransaction!.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT id
            FROM tramites.procedure_instance_biometric_validations
            WHERE estado = @estado
              AND intentos < max_intentos
            ORDER BY created_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "estado";
        p.Value = BiometricEstados.PendienteEnvio;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? reader.GetGuid(0) : null;
    }
}

/// <summary>Logging source-generated (CA1848) del worker de reintento de envío.</summary>
internal static partial class SendRetryLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Cola envío identidad: validación {ValidationId} ({Provider}) enviada tras {Intentos} intento(s) → en_proceso.")]
    public static partial void Sent(ILogger logger, Guid validationId, string provider, int intentos);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Cola envío identidad: reintento {Intentos} fallido para {ValidationId} ({Provider}); se reintentará.")]
    public static partial void RetryScheduled(ILogger logger, Guid validationId, string provider, int intentos, Exception ex);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Cola envío identidad: {ValidationId} ({Provider}) a error_envio tras {Intentos} intento(s) (transitorio={Transient}). Requiere acción.")]
    public static partial void DeadLettered(ILogger logger, Guid validationId, string provider, int intentos, bool transient);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola envío identidad: {ValidationId} sin proveedor registrado '{Provider}' → error_envio.")]
    public static partial void NoProvider(ILogger logger, Guid validationId, string provider);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Cola envío identidad: error en el ciclo de sondeo; se reintentará.")]
    public static partial void CycleError(ILogger logger, Exception ex);
}
