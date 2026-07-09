namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Respuesta de configuración operativa del tenant (GET y PUT, AC1/AC3).
/// Serializada en camelCase. Los enums se exponen en su formato wire
/// (<c>enrutamientoSMTP</c>: FLIT_SMTP|TENANT_API; <c>notificationTarget</c>:
/// COMPRADOR|RADICADOR|NINGUNO).
/// </summary>
public sealed record TenantSettingsResponse(
    Guid TenantId,
    SwitchesMatricula SwitchesMatricula,
    bool BaulFirmasActivo,
    string EnrutamientoSMTP,
    string NotificationTarget,
    IReadOnlyList<string> MetodosRecaudo,
    int RuntFailoverTimeoutMs,
    IReadOnlyDictionary<string, ConsultationProviderChoice> ConsultationProviderConfig);
