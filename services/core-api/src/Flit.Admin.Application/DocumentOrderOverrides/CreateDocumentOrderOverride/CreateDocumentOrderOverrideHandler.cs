using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.DocumentTypes;

namespace Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;

/// <summary>
/// Caso de uso de alta de un override de orden documental (HU #10196, AC1/AC2 / RF09–RF16).
///
/// Flujo: (1) valida <c>orden</c> → 422; (2) el tipo de trámite debe existir → 404;
/// (3) el tipo de documento debe existir → 404 y estar asociado al trámite → 422;
/// (4) la referencia del scope debe venir → 422 y existir → 404 (OT vía catálogo,
/// CLIENTE vía catálogo de tenants); (5) la tupla única (trámite, documento, scope,
/// referencia) no debe estar duplicada → 422; (6) crea el override.
/// </summary>
public sealed class CreateDocumentOrderOverrideHandler
{
    private readonly IDocumentOrderOverrideRepository _overrides;
    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly IDocumentTypeRepository _documentTypes;
    private readonly IProcedureDocumentRequirementRepository _requirements;
    private readonly ITransitOfficeCatalog _transitOffices;
    private readonly ITenantCatalog _tenants;

    public CreateDocumentOrderOverrideHandler(
        IDocumentOrderOverrideRepository overrides,
        IProcedureTypeCatalog procedureTypes,
        IDocumentTypeRepository documentTypes,
        IProcedureDocumentRequirementRepository requirements,
        ITransitOfficeCatalog transitOffices,
        ITenantCatalog tenants)
    {
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _documentTypes = documentTypes ?? throw new ArgumentNullException(nameof(documentTypes));
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
        _transitOffices = transitOffices ?? throw new ArgumentNullException(nameof(transitOffices));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    public async Task<CreateDocumentOrderOverrideResult> HandleAsync(
        CreateDocumentOrderOverrideCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;

        var error = DocumentOrderOverrideValidator.Validate(request.Orden);
        if (error is not null)
        {
            return CreateDocumentOrderOverrideResult.ValidationFailed(error);
        }

        var procedureExists = await _procedureTypes
            .ExistsAsync(request.ProcedureTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!procedureExists)
        {
            return CreateDocumentOrderOverrideResult.ProcedureTypeNotFound;
        }

        var document = await _documentTypes
            .GetByIdAsync(request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return CreateDocumentOrderOverrideResult.DocumentTypeNotFound;
        }

        var associated = await _requirements
            .ExistsPairAsync(request.ProcedureTypeId, request.DocumentTypeId, cancellationToken)
            .ConfigureAwait(false);
        if (!associated)
        {
            return CreateDocumentOrderOverrideResult.ValidationFailed(
                "El documento no está asociado a este tipo de trámite y no admite un override de orden.");
        }

        var (scopeRefError, scopeRefNotFound, scopeRefId) = await ResolveScopeRefAsync(command, cancellationToken)
            .ConfigureAwait(false);
        if (scopeRefError is not null)
        {
            return CreateDocumentOrderOverrideResult.ValidationFailed(scopeRefError);
        }

        if (scopeRefNotFound)
        {
            return CreateDocumentOrderOverrideResult.ScopeRefNotFound;
        }

        var duplicate = await _overrides
            .ExistsUniqueAsync(request.ProcedureTypeId, request.DocumentTypeId, command.Scope, scopeRefId, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate)
        {
            return CreateDocumentOrderOverrideResult.ValidationFailed(
                "Ya existe un override de orden para esa combinación de trámite, documento y ámbito.");
        }

        var created = await _overrides
            .CreateAsync(
                request.ProcedureTypeId,
                request.DocumentTypeId,
                command.Scope,
                scopeRefId,
                (short)request.Orden!.Value,
                command.CreatedBy,
                cancellationToken)
            .ConfigureAwait(false);

        return CreateDocumentOrderOverrideResult.Created(DocumentOrderOverrideResponse.From(created));
    }

    /// <summary>
    /// Resuelve la referencia del scope: devuelve un mensaje de validación (422) si la
    /// referencia obligatoria falta, marca <c>notFound</c> (404) si no existe en su
    /// catálogo, o el id válido en caso contrario.
    /// </summary>
    private async Task<(string? Error, bool NotFound, Guid ScopeRefId)> ResolveScopeRefAsync(
        CreateDocumentOrderOverrideCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (command.Scope == DocumentOrderScope.Ot)
        {
            if (request.TransitOfficeId is null || request.TransitOfficeId == Guid.Empty)
            {
                return ("El transitOfficeId es obligatorio para overrides de scope OT.", false, Guid.Empty);
            }

            return _transitOffices.Exists(request.TransitOfficeId.Value)
                ? (null, false, request.TransitOfficeId.Value)
                : (null, true, Guid.Empty);
        }

        // Scope CLIENTE.
        if (request.ClienteId is null || request.ClienteId == Guid.Empty)
        {
            return ("El clienteId es obligatorio para overrides de scope CLIENTE.", false, Guid.Empty);
        }

        var tenantExists = await _tenants
            .ExistsAsync(request.ClienteId.Value, cancellationToken)
            .ConfigureAwait(false);

        return tenantExists
            ? (null, false, request.ClienteId.Value)
            : (null, true, Guid.Empty);
    }
}
