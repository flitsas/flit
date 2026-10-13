using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Infrastructure.OtClientProcedures;

/// <summary>HU #13389 — ver <see cref="IOtClientProcedureConsolidadoContext"/>.</summary>
public sealed class OtClientProcedureConsolidadoContext(
    IOtClientProcedureRepository repository,
    IResolvedDocumentMatrixResolver matrixResolver) : IOtClientProcedureConsolidadoContext
{
    private readonly IOtClientProcedureRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly IResolvedDocumentMatrixResolver _matrixResolver =
        matrixResolver ?? throw new ArgumentNullException(nameof(matrixResolver));

    /// <summary>
    /// Mismo read que hacía <c>AdminOtEndpoints.ResolveClientProcedureAccessAsync</c> tras resolver el tenant
    /// y el organismo: <see cref="IOtClientProcedureRepository.GetByIdAsync(Guid, Guid, Guid?, CancellationToken)"/>.
    /// </summary>
    public Task<OtClientProcedure?> ResolverAccesoAsync(
        Guid otTenantId,
        Guid procedureInstanceId,
        Guid? transitOfficeIdOverride,
        CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(otTenantId, procedureInstanceId, transitOfficeIdOverride, cancellationToken);

    public Task<T> EjecutarEnContextoClienteAsync<T>(
        OtClientProcedure access,
        Func<IReadOnlyList<string>, Task<T>> accion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accion);
        return EjecutarEnContextoClienteAsync(
            access,
            resolverPrecedencia: true,
            precedencia => accion(precedencia ?? []),
            cancellationToken);
    }

    public Task<T> EjecutarEnContextoClienteAsync<T>(
        OtClientProcedure access,
        bool resolverPrecedencia,
        Func<IReadOnlyList<string>?, Task<T>> accion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(accion);

        // La compensación post-commit (HU #12797) la aporta la transacción gestionada del repositorio.
        return _repository.ExecuteInClientTenantScopeAsync(
            access.ClientTenantId,
            async () =>
            {
                var precedencia = resolverPrecedencia
                    ? await ResolverPrecedenciaMatrizAsync(access, cancellationToken).ConfigureAwait(false)
                    : null;
                return await accion(precedencia).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>
    /// Orden de la matriz documental resuelta del trámite con la precedencia del OT (HU #10706 AC1). Se
    /// llama DENTRO del scope RLS del tenant cliente (los requisitos base viven en tramites del
    /// cliente). Si el resolver falla o no hay matriz configurada, la lista vacía hace que el handler
    /// caiga al orden por modalidad. Movido tal cual desde <c>AdminOtEndpoints</c> (HU #13389).
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolverPrecedenciaMatrizAsync(
        OtClientProcedure access,
        CancellationToken cancellationToken)
    {
        try
        {
            var matriz = await _matrixResolver
                .ResolveAsync(access.ProcedureTypeId, access.TransitOfficeId, cancellationToken)
                .ConfigureAwait(false);
            return matriz.Select(m => m.Codigo).ToList();
        }
        catch
        {
            return [];
        }
    }
}
