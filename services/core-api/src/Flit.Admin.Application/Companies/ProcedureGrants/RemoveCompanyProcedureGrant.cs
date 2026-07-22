using Flit.Admin.Domain.Companies.ProcedureGrants;

namespace Flit.Admin.Application.Companies.ProcedureGrants;

/// <summary>Comando para deshabilitar un grant de tipo de trámite de una compañía.</summary>
public sealed class RemoveCompanyProcedureGrantCommand
{
    public required Guid TenantId { get; init; }

    public required Guid ProcedureTypeId { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que deshabilita. Opcional.</summary>
    public Guid? ChangedBy { get; init; }
}

/// <summary>
/// Caso de uso de la baja de un grant (FEATURE-08). Delega borrado + audit al repo. Devuelve
/// <c>false</c> si el grant no existía (→ 404).
/// </summary>
public sealed class RemoveCompanyProcedureGrantHandler
{
    private readonly ICompanyProcedureGrantRepository _repository;

    public RemoveCompanyProcedureGrantHandler(ICompanyProcedureGrantRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<bool> HandleAsync(
        RemoveCompanyProcedureGrantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _repository
            .RemoveGrantAsync(command.TenantId, command.ProcedureTypeId, command.ChangedBy, cancellationToken)
            .ConfigureAwait(false);
    }
}
