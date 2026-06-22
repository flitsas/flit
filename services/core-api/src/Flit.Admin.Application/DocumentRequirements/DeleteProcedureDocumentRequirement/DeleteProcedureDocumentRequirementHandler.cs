using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentRequirements.DeleteProcedureDocumentRequirement;

/// <summary>
/// Caso de uso de borrado físico de una asociación (HU #10195, AC4 / RF08).
///
/// Flujo: (1) existencia → 404; (2) guard de uso: si está en uso en trámites activos
/// → 409 (no se borra); (3) borrado físico de la fila. El guard real llega con la
/// HU #10197; hasta entonces el stub permite el borrado.
/// </summary>
public sealed class DeleteProcedureDocumentRequirementHandler
{
    private readonly IProcedureDocumentRequirementRepository _repository;
    private readonly IProcedureDocumentRequirementUsageGuard _usageGuard;

    public DeleteProcedureDocumentRequirementHandler(
        IProcedureDocumentRequirementRepository repository,
        IProcedureDocumentRequirementUsageGuard usageGuard)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _usageGuard = usageGuard ?? throw new ArgumentNullException(nameof(usageGuard));
    }

    public async Task<DeleteProcedureDocumentRequirementResult> HandleAsync(
        DeleteProcedureDocumentRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return DeleteProcedureDocumentRequirementResult.NotFound;
        }

        if (await _usageGuard.IsInUseAsync(command.Id, cancellationToken).ConfigureAwait(false))
        {
            return DeleteProcedureDocumentRequirementResult.InUse;
        }

        var deleted = await _repository.DeleteAsync(command.Id, cancellationToken).ConfigureAwait(false);

        return deleted
            ? DeleteProcedureDocumentRequirementResult.Deleted
            : DeleteProcedureDocumentRequirementResult.NotFound;
    }
}
