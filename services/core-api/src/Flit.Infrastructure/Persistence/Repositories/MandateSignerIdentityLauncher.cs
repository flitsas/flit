using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Tramites.Application.UseCases.Persons;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — lanza la validación de identidad propia de un mandatario reutilizando el
/// handler de prevalidación del trámite (<see cref="IniciarPrevalidacionHandler.HandleMandatarioAsync"/>): mismo proveedor
/// (Kyverum o mock), mismo enlace por correo, mismo webhook y misma outbox. Aquí solo se orquesta:
/// <list type="bullet">
///   <item>La validación se registra en el tenant de la COMPAÑÍA del mandatario. Quien dispara puede ser un usuario del
///        organismo (hub OT): dentro de una transacción propia se fija <c>app.current_tenant_id</c> al de la compañía
///        (<c>set_config</c> local a la transacción, parametrizado) para que la fila nazca ahí y no en el del organismo.</item>
///   <item>El alta del mandatario NUNCA falla por el proveedor: cualquier excepción inesperada se registra sin PII y se
///        devuelve <see cref="MandateSignerIdentityLaunchOutcome.Failed"/>; el reenvío queda disponible.</item>
/// </list>
/// </summary>
internal sealed class MandateSignerIdentityLauncher(
    FlitDbContext db,
    IniciarPrevalidacionHandler handler,
    ILogger<MandateSignerIdentityLauncher> logger) : IMandateSignerIdentityLauncher
{
    public async Task<MandateSignerIdentityLaunchResult> LaunchAsync(
        MandateSignerIdentityLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (!db.Database.IsRelational())
            {
                return await RunAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (db.Database.CurrentTransaction is not null)
            {
                // Transacción de quien llama: no se toca su contexto de tenant.
                return await RunAsync(request, cancellationToken).ConfigureAwait(false);
            }

            // El DbContext usa EnableRetryOnFailure: la transacción manual va dentro de la estrategia de ejecución.
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (tx.ConfigureAwait(false))
                {
                    await db.Database
                        .ExecuteSqlInterpolatedAsync(
                            $"SELECT set_config('app.current_tenant_id', {request.TenantId.ToString()}, true)",
                            cancellationToken)
                        .ConfigureAwait(false);
                    var result = await RunAsync(request, cancellationToken).ConfigureAwait(false);
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin PII: solo el id de la ficha y el tipo de excepción.
            MandateSignerIdentityLog.LaunchFailed(logger, request.MandateSignerId, ex);
            db.ChangeTracker.Clear();
            return new MandateSignerIdentityLaunchResult(MandateSignerIdentityLaunchOutcome.Failed, null, "error_inesperado");
        }
    }

    private async Task<MandateSignerIdentityLaunchResult> RunAsync(
        MandateSignerIdentityLaunchRequest request, CancellationToken cancellationToken)
    {
        var (result, error) = await handler
            .HandleMandatarioAsync(
                request.TenantId,
                request.MandateSignerId,
                request.DocumentType,
                request.DocumentNumber,
                request.FullName,
                request.Email,
                cancellationToken)
            .ConfigureAwait(false);

        if (error is null && result is not null)
        {
            return new MandateSignerIdentityLaunchResult(
                result.Queued ? MandateSignerIdentityLaunchOutcome.Queued : MandateSignerIdentityLaunchOutcome.Sent,
                result.Validation.Id);
        }

        return error == "prevalidacion_activa"
            ? new MandateSignerIdentityLaunchResult(MandateSignerIdentityLaunchOutcome.AlreadyInFlight, null, error)
            : new MandateSignerIdentityLaunchResult(MandateSignerIdentityLaunchOutcome.Failed, null, error);
    }
}

/// <summary>Logging source-generated (CA1848) del lanzamiento de la validación del mandatario.</summary>
internal static partial class MandateSignerIdentityLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo lanzar la validación de identidad del mandatario {MandateSignerId}.")]
    public static partial void LaunchFailed(ILogger logger, Guid mandateSignerId, Exception ex);
}
