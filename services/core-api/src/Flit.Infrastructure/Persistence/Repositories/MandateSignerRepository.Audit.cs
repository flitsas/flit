using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13138 (Feature #13115) — bitácora del ciclo de vida del mandatario: baja, eliminación, retiro de default,
/// reasignación de trámite y reactivación, con rol y módulo del actor. Se escribe en la MISMA transacción que la
/// operación (una operación que falla no deja registro) y sin datos personales del mandatario más allá de su id.
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
        var (actorRole, actorModule) = ActorInfo(actor);

        AddLifecycleAudit(
            otTenantId,
            delete ? "deleted" : "deactivated",
            delete ? AuditVocabulary.Operations.Delete : AuditVocabulary.Operations.Deactivate,
            signerId,
            now,
            changedBy,
            correlationId,
            new
            {
                mandateSignerId = signerId,
                actorRole,
                actorModule,
                offices = retiredOffices,
                links = retiredLinks.Select(l => new { officeId = l.TransitOfficeId, companyTenantId = l.CompanyTenantId }),
                defaults = retiredDefaults.Select(DefaultPayload),
                reassigned = reassignment.Reassigned,
                pendingOtDecision = reassignment.Pending,
            });

        foreach (var retired in retiredDefaults)
        {
            AddLifecycleAudit(
                otTenantId,
                "default_removed",
                AuditVocabulary.Operations.RemoveDefault,
                signerId,
                now,
                changedBy,
                correlationId,
                new
                {
                    mandateSignerId = signerId,
                    actorRole,
                    actorModule,
                    kind = retired.Kind,
                    transitOfficeId = retired.TransitOfficeId,
                    companyTenantId = retired.CompanyTenantId,
                });
        }

        foreach (var move in reassignment.Moves)
        {
            AddLifecycleAudit(
                otTenantId,
                "procedure_reassigned",
                AuditVocabulary.Operations.ReassignProcedure,
                signerId,
                now,
                changedBy,
                correlationId,
                new
                {
                    mandateSignerId = signerId,
                    actorRole,
                    actorModule,
                    procedureInstanceId = move.ProcedureInstanceId,
                    previousSignerId = move.PreviousSignerId,
                    newSignerId = move.NewSignerId,
                    pendingOtDecision = move.IsPending,
                });
        }
    }

    private void WriteReactivationAudit(
        ReactivateMandateSignerData data,
        Guid signerId,
        DateTimeOffset now,
        IReadOnlyList<MandateSignerLinkRef> restored,
        IReadOnlyList<MandateSignerLinkRef> conflicts,
        IReadOnlyList<MandateSignerDefaultRef> restoredDefaults,
        bool fromSnapshot)
    {
        var (actorRole, actorModule) = ActorInfo(data.ActorKind);
        AddLifecycleAudit(
            data.OtTenantId,
            "reactivated",
            AuditVocabulary.Operations.Reactivate,
            signerId,
            now,
            data.ChangedBy,
            data.CorrelationId,
            new
            {
                mandateSignerId = signerId,
                actorRole,
                actorModule,
                restoredFromSnapshot = fromSnapshot,
                restoredLinks = restored.Select(l => new { officeId = l.TransitOfficeId, companyTenantId = l.CompanyTenantId }),
                conflictLinks = conflicts.Select(l => new { officeId = l.TransitOfficeId, companyTenantId = l.CompanyTenantId }),
                restoredDefaults = restoredDefaults.Select(DefaultPayload),
            });
    }

    private static (string ActorRole, string ActorModule) ActorInfo(MandateSignerActorKind actor) =>
        (MandateSignerOriginRules.AuditRole(actor), MandateSignerOriginRules.AuditModule(actor));

    private static object DefaultPayload(MandateSignerDefaultRef d) => new
    {
        kind = d.Kind,
        officeId = d.TransitOfficeId,
        companyTenantId = d.CompanyTenantId,
    };

    /// <summary>
    /// HU #13138 — evento estructurado de ciclo de vida: módulo <c>mandatarios</c>, operación explícita, entidad
    /// objetivo = el mandatario y un detalle JSON con rol y módulo del actor. Sin datos personales del mandatario
    /// más allá de su identificador. Se escribe en la MISMA transacción: una operación que falla no deja registro.
    /// </summary>
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
            Result = AuditVocabulary.Results.Success,
            Operation = operation,
            Module = AuditVocabulary.Modules.Mandatarios,
            TargetEntityType = "MANDATE_SIGNER",
            TargetEntityId = signerId,
        });
    }
}
