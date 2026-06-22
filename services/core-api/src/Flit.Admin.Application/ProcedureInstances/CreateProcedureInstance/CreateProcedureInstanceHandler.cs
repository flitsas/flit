using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.ProcedureSnapshots;

namespace Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;

/// <summary>
/// Caso de uso del alta de un trámite con snapshot documental inmutable (HU #10197,
/// RF19 / AC1 / AC3).
///
/// Flujo: (1) valida el cuerpo (referencia, usuario); (2) el tipo de trámite debe existir
/// → 404; (3) el tenant/cliente debe existir → 404; (4) la referencia debe ser única por
/// tenant → 422; (5) resuelve la matriz documental vigente (precedencia Cliente &gt; OT &gt;
/// Default) y la congela en JSON; (6) persiste trámite + snapshot en una sola transacción.
/// Si la persistencia falla, nada queda creado y se devuelve 500 (AC3).
/// </summary>
public sealed class CreateProcedureInstanceHandler
{
    private const int ReferenceNumberMaxLength = 30;

    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly ITenantCatalog _tenants;
    private readonly IResolvedDocumentMatrixResolver _resolver;
    private readonly IProcedureInstanceRepository _repository;

    public CreateProcedureInstanceHandler(
        IProcedureTypeCatalog procedureTypes,
        ITenantCatalog tenants,
        IResolvedDocumentMatrixResolver resolver,
        IProcedureInstanceRepository repository)
    {
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateProcedureInstanceResult> HandleAsync(
        CreateProcedureInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var referenceNumber = request.ReferenceNumber?.Trim();
        if (string.IsNullOrWhiteSpace(referenceNumber))
        {
            return CreateProcedureInstanceResult.ValidationFailed(
                "El número de referencia (referenceNumber) es obligatorio.");
        }

        if (referenceNumber.Length > ReferenceNumberMaxLength)
        {
            return CreateProcedureInstanceResult.ValidationFailed(
                $"El número de referencia no puede exceder {ReferenceNumberMaxLength} caracteres.");
        }

        var createdByUserId =
            request.CreatedByUserId is { } bodyUser && bodyUser != Guid.Empty
                ? bodyUser
                : command.ActorUserId;
        if (createdByUserId is null || createdByUserId == Guid.Empty)
        {
            return CreateProcedureInstanceResult.ValidationFailed(
                "El usuario creador (createdByUserId) es obligatorio.");
        }

        if (!await _procedureTypes.ExistsAsync(request.ProcedureTypeId, cancellationToken).ConfigureAwait(false))
        {
            return CreateProcedureInstanceResult.ProcedureTypeNotFound;
        }

        if (!await _tenants.ExistsAsync(request.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return CreateProcedureInstanceResult.TenantNotFound;
        }

        if (await _repository
            .ReferenceNumberExistsAsync(request.TenantId, referenceNumber, cancellationToken)
            .ConfigureAwait(false))
        {
            return CreateProcedureInstanceResult.ValidationFailed(
                $"Ya existe un trámite con el número de referencia '{referenceNumber}' para este cliente.");
        }

        // Congela la matriz resuelta vigente en este instante (el tenant actúa como cliente
        // para la precedencia CLIENTE > OT > Default).
        var matrix = await _resolver
            .ResolveAsync(request.ProcedureTypeId, request.TransitOfficeId, request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var payload = new ProcedureDocumentSnapshotPayload(
            request.ProcedureTypeId,
            request.TransitOfficeId,
            request.TenantId,
            [.. matrix.Select(ToSnapshotItem)]);

        var snapshotJson = SnapshotJson.Serialize(payload);

        var newInstance = new NewProcedureInstance(
            request.TenantId,
            request.ProcedureTypeId,
            referenceNumber,
            request.TransitOfficeId,
            createdByUserId.Value);

        try
        {
            var created = await _repository
                .CreateWithSnapshotAsync(newInstance, snapshotJson, createdByUserId, cancellationToken)
                .ConfigureAwait(false);

            return CreateProcedureInstanceResult.Created(
                ProcedureInstanceResponse.From(created, payload.Documentos.Count));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AC3: cualquier fallo al persistir el trámite o el snapshot revierte toda la
            // operación (un solo SaveChangesAsync) → 500 y ningún registro creado.
            return CreateProcedureInstanceResult.Failed(
                "No se pudo crear el trámite: la generación del snapshot documental falló y la operación fue revertida.");
        }
    }

    private static ProcedureDocumentSnapshotItem ToSnapshotItem(ResolvedDocumentMatrixItem item) =>
        new(
            item.DocumentTypeId,
            item.Codigo,
            item.Nombre,
            item.Obligatorio,
            item.OrdenResuelto,
            item.NivelAplicado);
}
