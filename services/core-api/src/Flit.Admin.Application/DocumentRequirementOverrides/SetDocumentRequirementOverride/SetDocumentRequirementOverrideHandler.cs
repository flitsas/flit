using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.DocumentRequirementOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;

/// <summary>
/// Caso de uso de upsert/limpiar de la obligatoriedad documental por OT (HU #10198).
///
/// Flujo: (1) normaliza/valida el estado → 422; (2) el tipo de trámite debe existir → 404;
/// (3) el tipo de documento debe existir → 404 y estar asociado al trámite → 422; (4) el OT
/// debe existir → 404; (5) si estado=DEFAULT limpia el override (204), si no lo inserta o
/// actualiza (200). Granular solo para OT (no hay scope Cliente).
/// </summary>
public sealed class SetDocumentRequirementOverrideHandler
{
    private readonly IDocumentRequirementOverrideRepository _overrides;
    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IProcedureDocumentRequirementRepository _requirements;
    private readonly ITransitOfficeCatalog _transitOffices;

    public SetDocumentRequirementOverrideHandler(
        IDocumentRequirementOverrideRepository overrides,
        IProcedureTypeCatalog procedureTypes,
        IDocumentTypeRepository documentTypes,
        IProcedureDocumentRequirementRepository requirements,
        ITransitOfficeCatalog transitOffices)
    {
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _documentTypes = documentTypes ?? throw new ArgumentNullException(nameof(documentTypes));
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        _transitOffices = transitOffices ?? throw new ArgumentNullException(nameof(transitOffices));
    }

    public async Task<SetDocumentRequirementOverrideResult> HandleAsync(
        SetDocumentRequirementOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var state = DocumentRequirementState.Normalize(request.Estado);
        if (state is null)
        {
            return SetDocumentRequirementOverrideResult.ValidationFailed(
                "El estado debe ser REQUIRED, OPTIONAL, NOT_APPLICABLE o DEFAULT.");
        }

        if (request.TransitOfficeId == Guid.Empty)
        {
            return SetDocumentRequirementOverrideResult.ValidationFailed(
                "El transitOfficeId es obligatorio.");
        }

        var procedureExists = await _procedureTypes
            .ExistsAsync(request.ProcedureTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!procedureExists)
        {
            return SetDocumentRequirementOverrideResult.ProcedureTypeNotFound;
        }

        var document = await _documentTypes
            .GetByIdAsync(request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return SetDocumentRequirementOverrideResult.DocumentTypeNotFound;
        }

        var associated = await _requirements
            .ExistsPairAsync(request.ProcedureTypeId, request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!associated)
        {
            return SetDocumentRequirementOverrideResult.ValidationFailed(
                "El documento no está asociado a este tipo de trámite y no admite override de obligatoriedad.");
        }

        if (!_transitOffices.Exists(request.TransitOfficeId))
        {
            return SetDocumentRequirementOverrideResult.TransitOfficeNotFound;
        }

        // DEFAULT = limpiar el override → el documento vuelve a heredar el default del trámite.
        if (state == DocumentRequirementState.Default)
        {
            await _overrides
                .ClearAsync(request.ProcedureTypeId, request.DocumentTypeId, request.TransitOfficeId, cancellationToken)
                .ConfigureAwait(false);
            return SetDocumentRequirementOverrideResult.Cleared;
        }

        var saved = await _overrides
            .UpsertAsync(
                request.ProcedureTypeId,
                request.DocumentTypeId,
                request.TransitOfficeId,
                state,
                command.Actor,
                cancellationToken)
            .ConfigureAwait(false);

        return SetDocumentRequirementOverrideResult.Set(DocumentRequirementOverrideResponse.From(saved));
    }
}
