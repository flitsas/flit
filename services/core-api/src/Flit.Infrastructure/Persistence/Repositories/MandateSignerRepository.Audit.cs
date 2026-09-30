using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Traza de la baja: el evento <c>deactivated</c>/<c>deleted</c> anota qué vínculos, organismos y defaults retiró la
/// baja (insumo de la reactivación, HU #13136). La bitácora completa con rol y módulo es la HU #13138.
/// </summary>
internal sealed partial class MandateSignerRepository
{
    private void WriteRetirementAudit(
        Guid otTenantId,
        Guid signerId,
        bool delete,
        DateTimeOffset now,
        Guid? changedBy,
        Guid? correlationId,
        MandateSignerActorKind actor,
        IReadOnlyList<Guid> retiredOffices,
        IReadOnlyList<MandateSignerLinkRef> retiredLinks,
        IReadOnlyList<MandateSignerDefaultRef> retiredDefaults,
        MandateSignerReassignmentResult reassignment)
    {
        AddLifecycleAudit(
            otTenantId,
            delete ? "deleted" : "deactivated",
            delete ? "delete" : "deactivate",
            signerId,
            now,
            changedBy,
            correlationId,
            new
            {
                mandateSignerId = signerId,
                offices = retiredOffices,
                links = retiredLinks.Select(l => new { officeId = l.TransitOfficeId, companyTenantId = l.CompanyTenantId }),
                defaults = retiredDefaults.Select(DefaultPayload),
            });
    }

    private static void WriteReactivationAudit(
        ReactivateMandateSignerData data,
        Guid signerId,
        DateTimeOffset now,
        IReadOnlyList<MandateSignerLinkRef> restored,
        IReadOnlyList<MandateSignerLinkRef> conflicts,
        IReadOnlyList<MandateSignerDefaultRef> restoredDefaults,
        bool fromSnapshot)
    {
        // La bitácora de la reactivación llega con la HU #13138.
    }

    private static object DefaultPayload(MandateSignerDefaultRef d) => new
    {
        kind = d.Kind,
        officeId = d.TransitOfficeId,
        companyTenantId = d.CompanyTenantId,
    };

    private void AddLifecycleAudit(
        Guid otTenantId,
        string fieldName,
        string operation,
        Guid signerId,
        DateTimeOffset changedAt,
        Guid? changedBy,
        Guid? correlationId,
        object payload)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            EntityName = EntityName,
            FieldName = fieldName,
            OldValue = null,
            NewValue = JsonSerializer.Serialize(payload),
            ChangedAt = changedAt,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
            Result = "success",
            Operation = operation,
            Module = "mandatarios",
            TargetEntityType = "MANDATE_SIGNER",
            TargetEntityId = signerId,
        });
    }
}
