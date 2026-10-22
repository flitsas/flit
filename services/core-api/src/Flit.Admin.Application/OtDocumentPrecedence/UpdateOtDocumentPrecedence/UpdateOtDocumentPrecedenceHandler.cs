using Flit.Admin.Application.Consolidados;
using Flit.Admin.Domain.OtDocumentPrecedence;

namespace Flit.Admin.Application.OtDocumentPrecedence.UpdateOtDocumentPrecedence;

public sealed class UpdateOtDocumentPrecedenceCommand
{
    public Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }

    public UpdateOtDocumentPrecedenceRequest Request { get; init; } = new();
}

public enum UpdateOtDocumentPrecedenceStatus
{
    Updated,
    ValidationFailed,
}

public sealed record FieldError(string Field, string Message);

public sealed class UpdateOtDocumentPrecedenceResult
{
    public UpdateOtDocumentPrecedenceStatus Status { get; init; }

    public IReadOnlyList<OtDocumentPrecedenceResponse> Data { get; init; } = Array.Empty<OtDocumentPrecedenceResponse>();

    public IReadOnlyList<FieldError> Errors { get; init; } = Array.Empty<FieldError>();

    public static UpdateOtDocumentPrecedenceResult Updated(IReadOnlyList<OtDocumentPrecedenceResponse> data) =>
        new() { Status = UpdateOtDocumentPrecedenceStatus.Updated, Data = data };

    public static UpdateOtDocumentPrecedenceResult ValidationFailed(params FieldError[] errors) =>
        new() { Status = UpdateOtDocumentPrecedenceStatus.ValidationFailed, Errors = errors };
}

/// <summary>Reordenamiento batch atómico de prelación documental (HU #10222 AC2).</summary>
public sealed class UpdateOtDocumentPrecedenceHandler
{
    private const int MaxBatchSize = 50;

    private readonly IOtDocumentPrecedenceRepository _repository;
    private readonly IConsolidadoInvalidacionMasiva? _invalidacion;

    /// <param name="invalidacion">
    /// HU #12789 — invalida en bloque los consolidados afectados. Opcional para no romper los
    /// llamadores que no lo necesitan; en DI siempre se inyecta.
    /// </param>
    public UpdateOtDocumentPrecedenceHandler(
        IOtDocumentPrecedenceRepository repository,
        IConsolidadoInvalidacionMasiva? invalidacion = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _invalidacion = invalidacion;
    }

    public async Task<UpdateOtDocumentPrecedenceResult> HandleAsync(
        UpdateOtDocumentPrecedenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        if (command.Request.ProcedureTypeId == Guid.Empty)
        {
            return UpdateOtDocumentPrecedenceResult.ValidationFailed(
                new FieldError("procedure_type_id", "PROCEDURE_TYPE_REQUIRED"));
        }

        if (command.Request.Items.Count == 0 || command.Request.Items.Count > MaxBatchSize)
        {
            return UpdateOtDocumentPrecedenceResult.ValidationFailed(
                new FieldError("items", "INVALID_BATCH_SIZE"));
        }

        var items = command.Request.Items.Select(i => new OtDocumentPrecedenceOrderItem
        {
            DocumentTypeId = i.DocumentTypeId,
            SortOrder = i.SortOrder,
        }).ToList();

        var updated = await _repository.ReorderBatchAsync(
            command.TenantId,
            command.Request.ProcedureTypeId,
            items,
            command.ChangedBy,
            cancellationToken).ConfigureAwait(false);

        if (updated is null)
        {
            // HU #11182 — el repositorio pasó a upsert: ya no falla porque el OT no hubiera
            // configurado nada antes (ese era el 422 que dejaba la pantalla inoperante). Solo
            // queda el caso de un documento que no existe en el catálogo.
            return UpdateOtDocumentPrecedenceResult.ValidationFailed(
                new FieldError("items", "UNKNOWN_DOCUMENT_TYPE"));
        }

        // HU #12789 AC1 — el nuevo orden cambia el PDF de los trámites de ese tipo radicados ante el
        // OT: se bajan sus banderas de vigencia en una sola operación y el siguiente acceso lo
        // reconstruye con la prelación nueva (ADR-0038).
        if (_invalidacion is not null)
        {
            await _invalidacion
                .InvalidarPorPrelacionOtAsync(command.TenantId, command.Request.ProcedureTypeId, cancellationToken)
                .ConfigureAwait(false);
        }

        return UpdateOtDocumentPrecedenceResult.Updated(updated.Select(OtDocumentPrecedenceMapper.ToResponse).ToList());
    }
}
