using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentRequirements.CreateProcedureDocumentRequirement;

/// <summary>
/// Caso de uso de alta de una asociación trámite ↔ documento (HU #10195, AC1/AC5 / RF05).
///
/// Flujo: (1) valida orden/obligatorio → 422; (2) el tipo de trámite debe existir → 404;
/// (3) el tipo de documento debe existir → 404 y estar activo → 422 (mensaje AC5);
/// (4) el par (trámite, documento) debe ser único → 422; (5) crea la asociación.
/// </summary>
public sealed class CreateProcedureDocumentRequirementHandler
{
    private readonly IProcedureDocumentRequirementRepository _requirements;
    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly IDocumentTypeRepository _documentTypes;

    public CreateProcedureDocumentRequirementHandler(
        IProcedureDocumentRequirementRepository requirements,
        IProcedureTypeCatalog procedureTypes,
        IDocumentTypeRepository documentTypes)
    {
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _documentTypes = documentTypes ?? throw new ArgumentNullException(nameof(documentTypes));
    }

    public async Task<CreateProcedureDocumentRequirementResult> HandleAsync(
        CreateProcedureDocumentRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var error = ProcedureDocumentRequirementValidator.Validate(request.OrdenDefault, request.Obligatorio);
        if (error is not null)
        {
            return CreateProcedureDocumentRequirementResult.ValidationFailed(error);
        }

        var procedureExists = await _procedureTypes
            .ExistsAsync(request.ProcedureTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!procedureExists)
        {
            return CreateProcedureDocumentRequirementResult.ProcedureTypeNotFound;
        }

        var document = await _documentTypes
            .GetByIdAsync(request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return CreateProcedureDocumentRequirementResult.DocumentTypeNotFound;
        }

        if (!document.IsActive)
        {
            return CreateProcedureDocumentRequirementResult.ValidationFailed(
                CreateProcedureDocumentRequirementResult.DocumentInactiveMessage);
        }

        var duplicate = await _requirements
            .ExistsPairAsync(request.ProcedureTypeId, request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            return CreateProcedureDocumentRequirementResult.ValidationFailed(
                "El documento ya está asociado a este tipo de trámite.");
        }

        var created = await _requirements
            .CreateAsync(
                request.ProcedureTypeId,
                request.DocumentTypeId,
                (short)request.OrdenDefault!.Value,
                request.Obligatorio!.Value,
                command.CreatedBy,
                cancellationToken)
            .ConfigureAwait(false);

        return CreateProcedureDocumentRequirementResult.Created(
            ProcedureDocumentRequirementResponse.From(created));
    }
}
