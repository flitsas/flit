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
        var seq = await repo.CountByTenantAndYearAsync(request.TenantId, year, ct) + 1;
        var reference = $"TRM-{year}-{seq:D6}";

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureTypeId = request.ProcedureTypeId,
            ReferenceNumber = reference,
            Status = ProcedureInstanceStatus.Draft,
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

        await repo.AddAsync(instance, ct);
        await repo.SaveChangesAsync(ct);

        return (ToSummary(instance), null);
    }

    internal static ProcedureInstanceSummary ToSummary(ProcedureInstance e) =>
        new(e.Id, e.ReferenceNumber, e.Status, e.ProcedureTypeId, e.TenantId, e.CreatedAt, e.SubmittedAt);
}
