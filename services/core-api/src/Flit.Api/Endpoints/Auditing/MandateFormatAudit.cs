using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// HU #13171 (Feature #13118, Épica #13090) — bitácora de la edición de los formatos de contrato de mandato por el
/// Super Admin, sobre <c>admin.tenant_config_audit_logs</c> vía <see cref="IAdminAuditWriter"/> (best-effort).
/// <list type="bullet">
///   <item>Éxito con cambio: actor, fecha, formato, nombre y tipo anterior y nuevo, y el número de versión publicada.</item>
///   <item>Sin cambio real: una fila de éxito con <c>changed = false</c> (el intento queda registrado).</item>
///   <item>Fallo (400/404/409): el intento con el código de error; el formato no se toca.</item>
/// </list>
/// Nunca se guarda el cuerpo de la plantilla (solo su número de versión) ni datos de personas.
/// </summary>
internal static class MandateFormatAudit
{
    public const string EntityName = "mandate_format";
    public const string TargetEntityType = "mandate_format";

    public static Task WriteAsync(
        HttpContext http,
        string code,
        UpdateMandateFormatRequest request,
        MandateFormatUpdateResult result)
    {
        var ok = result.Status == MandateFormatUpdateStatus.Ok;
        var previous = result.Previous;
        var current = result.Current ?? previous;

        var oldValue = JsonSerializer.Serialize(new
        {
            format = code,
            name = previous?.Name,
            assignmentMode = previous?.AssignmentMode,
            version = previous?.CurrentVersion,
        });
        var newValue = ok
            ? JsonSerializer.Serialize(new
            {
                format = code,
                name = current?.Name,
                assignmentMode = current?.AssignmentMode,
                version = current?.CurrentVersion,
                templatePublished = result.PublishedVersion is not null,
                changed = result.Changed,
            })
            : JsonSerializer.Serialize(new
            {
                format = code,
                attemptedName = request.Name,
                attemptedAssignmentMode = request.AssignmentMode,
                attemptedTemplate = request.Body is not null,
            });

        var writer = http.RequestServices.GetRequiredService<IAdminAuditWriter>();
        var auditContext = http.RequestServices.GetRequiredService<IAuditContextAccessor>();
        var actor = http.User.FindFirstValue("sub");

        return writer.WriteAsync(
            new AdminAuditEntry(
                TenantId: RequestTenantResolver.ResolveTenantIdOrNull(http.User),
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: EntityName,
                Operation: AuditVocabulary.Operations.Update,
                Result: ok ? AuditVocabulary.Results.Success : AuditVocabulary.Results.Failure,
                ErrorCode: ok ? null : result.ErrorCode ?? "formato_no_encontrado",
                ActorUserId: Guid.TryParse(actor, out var actorId) ? actorId : null,
                TargetEntityType: TargetEntityType,
                TargetEntityId: null,
                ClientIp: auditContext.ClientIp,
                UserAgent: http.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
                OldValue: oldValue,
                NewValue: newValue),
            http.RequestAborted);
    }
}
