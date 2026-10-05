using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// HU #13149 (Feature #13117, Épica #13090) — bitácora de los cambios del tipo de mandato de una compañía en un
/// organismo (Persona natural | Persona jurídica | Mandato abierto), escrita por el Super Admin desde Plataforma.
/// Sobre <c>admin.tenant_config_audit_logs</c> vía <see cref="IAdminAuditWriter"/> (best-effort: si la escritura
/// falla no rompe la respuesta).
/// <list type="bullet">
///   <item>Éxito con cambio real de tipo: actor, fecha, organismo, compañía, tipo anterior y tipo nuevo.</item>
///   <item>Restablecimiento (DELETE con regla propia): tipo anterior y <c>restoredToDefault = true</c>.</item>
///   <item>Fallo (400/404/409): el intento con su código de error; la regla no se toca.</item>
///   <item>Sin cambio real (mismo tipo vigente): no genera entrada.</item>
/// </list>
/// Solo se guardan TIPOS, organismo y compañía: nunca nombre, NIT ni documento de personas o de la entidad.
/// </summary>
internal static class MandateRuleTypeAudit
{
    public const string EntityName = "company_ot_mandate_rule";
    public const string TargetEntityType = "company";

    public static Task WriteSuccessAsync(
        HttpContext http,
        string operation,
        Guid officeId,
        Guid companyTenantId,
        MandateRuleTypeChange change)
    {
        if (!change.TypeChanged)
            return Task.CompletedTask;

        var restored = operation == AuditVocabulary.Operations.Delete;
        var oldValue = JsonSerializer.Serialize(new
        {
            officeId,
            companyTenantId,
            assignmentMode = change.PreviousMode,
            hadExplicitRule = change.HadExplicitRule,
        });
        var newValue = JsonSerializer.Serialize(new
        {
            officeId,
            companyTenantId,
            assignmentMode = change.NewMode,
            restoredToDefault = restored,
        });

        return WriteAsync(
            http, operation, AuditVocabulary.Results.Success, errorCode: null, companyTenantId, oldValue, newValue);
    }

    public static Task WriteFailureAsync(
        HttpContext http,
        string operation,
        Guid officeId,
        Guid companyTenantId,
        string errorCode,
        string? attemptedMode)
    {
        var attempted = JsonSerializer.Serialize(new
        {
            officeId,
            companyTenantId,
            attemptedAssignmentMode = attemptedMode,
        });

        return WriteAsync(
            http, operation, AuditVocabulary.Results.Failure, errorCode, companyTenantId, oldValue: null, attempted);
    }

    private static Task WriteAsync(
        HttpContext http,
        string operation,
        string result,
        string? errorCode,
        Guid companyTenantId,
        string? oldValue,
        string? newValue)
    {
        var writer = http.RequestServices.GetRequiredService<IAdminAuditWriter>();
        var auditContext = http.RequestServices.GetRequiredService<IAuditContextAccessor>();
        var actor = http.User.FindFirstValue("sub");

        return writer.WriteAsync(
            new AdminAuditEntry(
                TenantId: RequestTenantResolver.ResolveTenantIdOrNull(http.User),
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: EntityName,
                Operation: operation,
                Result: result,
                ErrorCode: errorCode,
                ActorUserId: Guid.TryParse(actor, out var actorId) ? actorId : null,
                TargetEntityType: TargetEntityType,
                TargetEntityId: companyTenantId,
                ClientIp: auditContext.ClientIp,
                UserAgent: http.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                OldValue: oldValue,
                NewValue: newValue),
            http.RequestAborted);
    }
}
