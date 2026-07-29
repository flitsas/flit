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
    IReadOnlyDictionary<string, ConsultationProviderChoice> ConsultationProviderConfig,
    // Feature #10587 — preasignación de placa por compañía.
    bool PreasignacionPlacaActiva,
    // Sub-flujo post-radicación: con placa completa, omitir Asignado → Terminado.
    bool PlateFlowSkipToTerminado,
    AvaluoProviderConfigDto AvaluoProviderConfig,
    // FEATURE 02 — fuente de comparendos (internal | external).
    string FinesQuerySource);
