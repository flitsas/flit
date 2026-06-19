using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record CreateProcedureInstanceRequest(
    Guid TenantId,
    Guid ProcedureTypeId,
    Guid CreatedByUserId,
    Guid? TransitOfficeId);

public sealed record ProcedureInstanceSummary(
    Guid Id,
    string ReferenceNumber,
    string Status,
    Guid ProcedureTypeId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt);

public sealed class CreateProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo)
{
    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        CreateProcedureInstanceRequest request,
        CancellationToken ct = default)
    {
        var procedureType = await typeRepo.GetByIdAsync(request.ProcedureTypeId, ct);
        if (procedureType is null)
            return (null, "not_found");

        if (procedureType.PublicationStatus != PublicationStatus.Published)
            return (null, "not_published");

        var now = DateTimeOffset.UtcNow;
        var year = now.Year;

        // Slice 4b: deriva modalidad/tipología desde la familia del tipo elegido para que el
        // wizard y el gating de documentos apliquen la modalidad correcta en runtime.
        var (modalidad, tipologia) = TipologiaResolver.FromFamily(procedureType.Family);

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureTypeId = request.ProcedureTypeId,
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

        var saved = await repo.AddWithUniqueReferenceAsync(instance, year, ct);
        if (!saved)
            return (null, "reference_conflict");

        return (ToSummary(instance), null);
    }

    internal static ProcedureInstanceSummary ToSummary(ProcedureInstance e) =>
        new(e.Id, e.ReferenceNumber, e.Status, e.ProcedureTypeId, e.TenantId, e.CreatedAt, e.SubmittedAt);
}
