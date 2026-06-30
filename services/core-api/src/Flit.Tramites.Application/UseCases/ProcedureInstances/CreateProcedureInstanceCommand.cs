using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record CreateProcedureInstanceRequest(
    Guid TenantId,
    Guid? ProcedureTypeId,
    Guid CreatedByUserId,
    Guid? TransitOfficeId,
    string? Modalidad = null);

public sealed record ProcedureInstanceSummary(
    Guid Id,
    string ReferenceNumber,
    string Status,
    Guid ProcedureTypeId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DraftFinalizedAt = null);

public sealed class CreateProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo)
{
    // M0: mapeo modalidad → código canónico del procedure_type sembrado (dev seed).
    // matricula_inicial → MATRICULA_NUEVA (familia MATRICULAS), traspaso → TRASPASO_STANDARD (familia TRASPASO).
    // Resolvemos por el código estable y publicado para que la selección sea determinista incluso
    // si en el futuro coexisten varios tipos publicados en la misma familia.
    private static readonly Dictionary<string, string> ModalidadToCanonicalCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TramiteModalidadEntradaCodes.MatriculaInicial] = "MATRICULA_NUEVA",
            [TramiteModalidadEntradaCodes.Traspaso] = "TRASPASO_STANDARD",
        };

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        CreateProcedureInstanceRequest request,
        CancellationToken ct = default)
    {
        var hasTypeId = request.ProcedureTypeId is { } id && id != Guid.Empty;
        var hasModalidad = !string.IsNullOrWhiteSpace(request.Modalidad);

        // Exactamente uno de {procedureTypeId, modalidad} debe venir.
        if (hasTypeId == hasModalidad)
            return (null, "invalid_request");

        ProcedureType? procedureType;
        if (hasTypeId)
        {
            procedureType = await typeRepo.GetByIdAsync(request.ProcedureTypeId!.Value, ct);
            if (procedureType is null)
                return (null, "not_found");

            if (procedureType.PublicationStatus != PublicationStatus.Published)
                return (null, "not_published");
        }
        else
        {
            // Resolución por modalidad: si la modalidad no es canónica o no hay tipo publicado → no disponible.
            if (!ModalidadToCanonicalCode.TryGetValue(request.Modalidad!.Trim(), out var canonicalCode))
                return (null, "modalidad_not_available");

            procedureType = await typeRepo.GetByCodePublishedAsync(canonicalCode, ct);
            if (procedureType is null)
                return (null, "modalidad_not_available");
        }

        var now = DateTimeOffset.UtcNow;
        var year = now.Year;

        // Slice 4b: deriva modalidad/tipología desde la familia del tipo elegido para que el
        // wizard y el gating de documentos apliquen la modalidad correcta en runtime.
        var (modalidad, tipologia) = TipologiaResolver.FromFamily(procedureType.Family);

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureTypeId = procedureType.Id,
            ReferenceNumber = string.Empty, // generado de forma resiliente en el repo (retry ante colisión)
            Status = ProcedureInstanceStatus.Draft,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            TransitOfficeId = request.TransitOfficeId,
            CreatedByUserId = request.CreatedByUserId,
            CreatedAt = now,
            CreatedBy = request.CreatedByUserId
        };

        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureInstanceId = instance.Id,
            FromStatus = null,
            ToStatus = ProcedureInstanceStatus.Draft,
            ChangedAt = now,
            ChangedBy = request.CreatedByUserId
        });

        var outcome = await repo.AddWithUniqueReferenceAsync(instance, year, ct);
        return outcome switch
        {
            AddProcedureInstanceOutcome.ReferenceConflict => (null, "reference_conflict"),
            AddProcedureInstanceOutcome.ReferencedEntityMissing => (null, "invalid_reference"),
            _ => (ToSummary(instance), null),
        };
    }

    internal static ProcedureInstanceSummary ToSummary(ProcedureInstance e) =>
        new(e.Id, e.ReferenceNumber, e.Status, e.ProcedureTypeId, e.TenantId, e.CreatedAt, e.SubmittedAt, e.DraftFinalizedAt);
}
