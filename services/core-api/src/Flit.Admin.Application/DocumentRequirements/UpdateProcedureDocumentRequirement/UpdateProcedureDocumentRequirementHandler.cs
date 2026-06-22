using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentRequirements.UpdateProcedureDocumentRequirement;

/// <summary>
/// Caso de uso de actualización de una asociación (HU #10195, AC3 / RF07).
///
/// Flujo: (1) valida orden/obligatorio → 422; (2) actualiza si existe; el repositorio
/// devuelve null cuando el id no existe → 404. Solo se modifican el orden default y la
/// obligatoriedad; el par (trámite, documento) no se toca.
/// </summary>
public sealed class UpdateProcedureDocumentRequirementHandler
{
    private readonly IProcedureDocumentRequirementRepository _repository;

    public UpdateProcedureDocumentRequirementHandler(IProcedureDocumentRequirementRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateProcedureDocumentRequirementResult> HandleAsync(
        UpdateProcedureDocumentRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var error = ProcedureDocumentRequirementValidator.Validate(request.OrdenDefault, request.Obligatorio);
        if (error is not null)
        {
            return UpdateProcedureDocumentRequirementResult.ValidationFailed(error);
        }

        var updated = await _repository
            .UpdateAsync(
                command.Id,
                (short)request.OrdenDefault!.Value,
                request.Obligatorio!.Value,
                cancellationToken)
            .ConfigureAwait(false);

        return updated is null
            ? UpdateProcedureDocumentRequirementResult.NotFound
            : UpdateProcedureDocumentRequirementResult.Updated(
                ProcedureDocumentRequirementResponse.From(updated));
    }
}
