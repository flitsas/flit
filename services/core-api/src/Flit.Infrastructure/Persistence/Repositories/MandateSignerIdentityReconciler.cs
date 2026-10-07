using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consulta on-demand del estado de la validación propia de un mandatario reutilizando
/// <see cref="ReconciliarIdentidadHandler.HandleMandatarioAsync"/> (mismo GET a Kyverum y mismo applier que el trámite).
/// La validación vive en el tenant de la COMPAÑÍA y quien consulta puede ser un usuario del organismo (hub OT): igual que
/// <see cref="MandateSignerIdentityLauncher"/>, se corre en una transacción propia con <c>row_security</c> apagado para
/// encontrar la fila sin depender del tenant del usuario.
/// </summary>
internal sealed class MandateSignerIdentityReconciler(
    FlitDbContext db,
    ReconciliarIdentidadHandler handler) : IMandateSignerIdentityReconciler
{
    public async Task<MandateSignerIdentityReconcileResult> ReconcileAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                    .ConfigureAwait(false);
            }

            return await RunAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                    .ConfigureAwait(false);
                var result = await RunAsync(mandateSignerId, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private async Task<MandateSignerIdentityReconcileResult> RunAsync(
        Guid mandateSignerId, CancellationToken cancellationToken)
    {
        var (result, error) = await handler
            .HandleMandatarioAsync(mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        if (result is not null)
        {
            return new MandateSignerIdentityReconcileResult(
                MandateSignerIdentityReconcileOutcome.Ok, result.Status, result.Updated);
        }

        return error == "not_found"
            ? new MandateSignerIdentityReconcileResult(MandateSignerIdentityReconcileOutcome.SinValidacion)
            : new MandateSignerIdentityReconcileResult(MandateSignerIdentityReconcileOutcome.ProveedorNoDisponible);
    }
}
