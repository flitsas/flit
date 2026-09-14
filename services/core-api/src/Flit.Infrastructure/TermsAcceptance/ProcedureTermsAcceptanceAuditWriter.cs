using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.TermsAcceptance;
using Flit.Tramites.Domain.TermsAcceptance;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.TermsAcceptance;

/// <summary>
/// Refleja la aceptación de T&amp;C en el rastro administrativo unificado (<see cref="IAdminAuditWriter"/>
/// → <c>admin.tenant_config_audit_logs</c>), el mismo mecanismo que Notificaciones, FUR y Confirmación
/// RUNT, para que la pantalla de Auditoría del SuperAdmin la muestre con módulo <c>tramites</c> y
/// operación <c>accept_terms</c>. La fila de evidencia (hard-fail) ya quedó escrita antes de llegar aquí.
/// </summary>
internal sealed class ProcedureTermsAcceptanceAuditWriter(IAdminAuditWriter auditWriter, FlitDbContext db) : IProcedureTermsAcceptanceAuditWriter
{
    public const string EntityName = "procedure_terms_acceptance";
    public const string TargetEntityType = "PROCEDURE_TERMS_ACCEPTANCE";

    public async Task WriteAcceptedAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acceptance);

        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                acceptance.TenantId,
                TenantType: await ResolveTenantTypeAsync(acceptance.TenantId, ct).ConfigureAwait(false),
                AuditVocabulary.Modules.Tramites,
                EntityName,
                AuditVocabulary.Operations.AcceptTerms,
                AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: acceptance.UserId,
                TargetEntityType,
                TargetEntityId: acceptance.Id,
                acceptance.ClientIp,
                acceptance.UserAgent,
                NewValue: JsonSerializer.Serialize(new
                {
                    procedureTypeCode = acceptance.ProcedureTypeCode,
                    termsUrl = acceptance.TermsUrl,
                    acceptedAt = acceptance.AcceptedAt,
                })),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mismo criterio que <c>UserRoleAssignmentRepository.GetTenantTargetEntityTypeAsync</c>: un tenant
    /// con <c>TransitOfficeProfile</c> es <c>TRANSIT_OFFICE</c>; el resto, <c>COMPANY</c>. Sin tenant
    /// (SuperAdmin sin compañía acotada) no hay tipo que poner.
    /// </summary>
    private async Task<string?> ResolveTenantTypeAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null) return null;
        var isOt = await db.TransitOfficeProfiles.AsNoTracking().AnyAsync(p => p.TenantId == tenantId, ct).ConfigureAwait(false);
        return isOt ? "TRANSIT_OFFICE" : "COMPANY";
    }
}
