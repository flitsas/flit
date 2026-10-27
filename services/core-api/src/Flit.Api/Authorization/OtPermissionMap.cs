using System.Text.RegularExpressions;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13639 (Feature #13100 FA-4, Épica #12753) — slug OT que exige cada ruta del organismo, según la Parte 2 del
/// Anexo A (<c>docs/design/EPICA-12753-anexo-a-permisos-ot.md</c>): 90 rutas. Las demás rutas OT (SuperAdmin, 410 Gone y
/// la base <c>GET /admin/ot/profile</c> / <c>GET /admin/transit-offices</c>) no llevan slug y no aparecen aquí.
/// Lo aplica <see cref="OtPermissionEnforcementMiddleware"/> en AND con la policy de rol, solo con
/// <c>Security:OtPermissionEnforcement:Enabled=true</c>.
/// </summary>
public static partial class OtPermissionMap
{
    public const string Tramites = "ot.tramites.manage";
    public const string Improntas = "ot.improntas.manage";
    public const string Reportes = "ot.reportes.manage";
    public const string Mandatos = "ot.mandatos.manage";
    public const string Mandatarios = "ot.mandatarios.manage";
    public const string Documentos = "ot.documentos.manage";
    public const string Usuarios = "ot.usuarios.manage";
    public const string Integracion = "ot.integracion.manage";

    /// <summary>Firma normalizada (<c>VERBO /ruta</c>, ver <see cref="Signature"/>) → slug.</summary>
    public static IReadOnlyDictionary<string, string> BySignature { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ot.tramites.manage (29)
        ["GET /api/v1/admin/ot/client-procedures"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/fields"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/search"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/health"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/counters"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/counters"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/approve"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/reject"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/revocation-requests/approve"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/revocation-requests/reject"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/revocation-requests/active"] = Tramites,
        ["GET /api/v1/admin/ot/revocation-requests"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/consolidado"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/consolidado"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/consolidado-maestro"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/consolidado/entrega"] = Tramites,
        ["POST /api/v1/admin/ot/consolidados/lotes"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/documents"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/documents/{attachmentId}/preview-url"] = Tramites,
        ["GET /api/v1/admin/ot/client-procedures/{id}/documents/{attachmentId}/download"] = Tramites,
        ["POST /api/v1/admin/ot/client-procedures/{id}/attachments"] = Tramites,
        ["POST /api/v1/admin/plate-ranges/procedures/{instanceId}/assign-plate"] = Tramites,
        ["POST /api/v1/admin/plate-ranges/procedures/{instanceId}/release-plate"] = Tramites,
        ["POST /api/v1/admin/plate-ranges/procedures/{instanceId}/revoke"] = Tramites,
        ["POST /api/v1/admin/plate-ranges/procedures/{instanceId}/update-plate"] = Tramites,
        ["GET /api/v1/admin/transit-offices/{id}/quipux-submissions"] = Tramites,
        ["POST /api/v1/admin/transit-offices/{id}/quipux-submissions/{submissionId}/retry"] = Tramites,
        ["POST /api/v1/admin/transit-offices/{id}/quipux-submissions/{submissionId}/cancel"] = Tramites,
        // ot.improntas.manage (4)
        ["GET /api/v1/admin/ot/imprint-signatures"] = Improntas,
        ["POST /api/v1/admin/ot/imprint-signatures/{id}/validate"] = Improntas,
        ["GET /api/v1/admin/ot/imprint-signatures/{id}/preview-url"] = Improntas,
        ["GET /api/v1/admin/ot/imprint-signatures/{id}/validations"] = Improntas,
        // ot.reportes.manage (23)
        ["GET /api/v1/admin/ot/metrics/operational"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/performance"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/rejection-reasons"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/drilldown"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/report"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/reviewers"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/reviewer-options"] = Reportes,
        ["GET /api/v1/admin/ot/metrics/client-companies"] = Reportes,
        ["GET /api/v1/admin/ot/queries/fields"] = Reportes,
        ["POST /api/v1/admin/ot/queries/run"] = Reportes,
        ["GET /api/v1/admin/ot/queries/saved"] = Reportes,
        ["POST /api/v1/admin/ot/queries/saved"] = Reportes,
        ["DELETE /api/v1/admin/ot/queries/saved/{id}"] = Reportes,
        ["GET /api/v1/admin/ot/alert-rules"] = Reportes,
        ["POST /api/v1/admin/ot/alert-rules"] = Reportes,
        ["PUT /api/v1/admin/ot/alert-rules/{id}"] = Reportes,
        ["DELETE /api/v1/admin/ot/alert-rules/{id}"] = Reportes,
        ["GET /api/v1/admin/ot/alert-events"] = Reportes,
        ["POST /api/v1/admin/ot/alert-events/{id}/ack"] = Reportes,
        ["GET /api/v1/admin/ot/report-schedules"] = Reportes,
        ["POST /api/v1/admin/ot/report-schedules"] = Reportes,
        ["PUT /api/v1/admin/ot/report-schedules/{id}"] = Reportes,
        ["DELETE /api/v1/admin/ot/report-schedules/{id}"] = Reportes,
        // ot.mandatos.manage (11)
        ["GET /api/v1/admin/ot/offices/{officeId}/mandatos"] = Mandatos,
        ["PATCH /api/v1/admin/ot/offices/{officeId}/mandatos/default-signer"] = Mandatos,
        ["GET /api/v1/admin/ot/offices/{officeId}/mandatos/preview"] = Mandatos,
        ["GET /api/v1/admin/ot/offices/{officeId}/mandatos/company-rules"] = Mandatos,
        ["PATCH /api/v1/admin/ot/offices/{officeId}/mandatos/company-rules/{companyTenantId}/default-signer"] = Mandatos,
        ["DELETE /api/v1/admin/ot/offices/{officeId}/mandatos/company-rules/{companyTenantId}"] = Mandatos,
        ["GET /api/v1/admin/ot/offices/{officeId}/mandatos/templates/{templateCode}/preview"] = Mandatos,
        ["GET /api/v1/admin/ot/offices/{officeId}/mandatos/formatos"] = Mandatos,
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers"] = Mandatos,
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/companies"] = Mandatos,
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/signature-image"] = Mandatos,
        // ot.mandatarios.manage (11)
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/signature-vault"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/signature-vault"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers"] = Mandatarios,
        ["PUT /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/inactivate"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/reactivate"] = Mandatarios,
        ["DELETE /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}"] = Mandatarios,
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/impact"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/identity-validation/reconcile"] = Mandatarios,
        ["POST /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/identity-validation/resend"] = Mandatarios,
        ["GET /api/v1/admin/transit-offices/{transitOfficeId}/mandate-signers/associable-companies"] = Mandatarios,
        // ot.documentos.manage (2)
        ["GET /api/v1/admin/ot/document-precedence"] = Documentos,
        ["PATCH /api/v1/admin/ot/document-precedence"] = Documentos,
        // ot.usuarios.manage (6)
        ["POST /api/v1/admin/ot/users/invite"] = Usuarios,
        ["GET /api/v1/admin/ot/users"] = Usuarios,
        ["PATCH /api/v1/admin/ot/users/{userId}"] = Usuarios,
        ["POST /api/v1/admin/ot/invitations/{invitationId}/resend"] = Usuarios,
        ["DELETE /api/v1/admin/ot/invitations/{invitationId}"] = Usuarios,
        ["POST /api/v1/admin/ot/invitations/{invitationId}/reactivate"] = Usuarios,
        // ot.integracion.manage (4)
        ["POST /api/v1/admin/ot/webhooks"] = Integracion,
        ["GET /api/v1/admin/ot/webhooks"] = Integracion,
        ["PATCH /api/v1/admin/ot/webhooks/{id}"] = Integracion,
        ["GET /api/v1/admin/ot/api-logs"] = Integracion,
    };

    /// <summary>Firma de una ruta: verbo en mayúsculas, sin restricciones de parámetro (<c>{id:guid}</c> → <c>{id}</c>) ni "/" final.</summary>
    public static string Signature(string method, string? rawPattern)
    {
        var path = ConstraintRegex().Replace(rawPattern ?? string.Empty, "{$1}");
        if (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
        return $"{method.ToUpperInvariant()} {path}";
    }

    [GeneratedRegex(@"\{(\w+)(?::[^}]*)?\}")]
    private static partial Regex ConstraintRegex();
}
