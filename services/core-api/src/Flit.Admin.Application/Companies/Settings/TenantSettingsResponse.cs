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
    // Validación del SOAT ante el RUNT al «Enviar al OT» (estado asignado, ADR-0059).
    bool ValidarSoatConRunt,
    AvaluoProviderConfigDto AvaluoProviderConfig,
    // FEATURE 02 — fuente de comparendos (internal | external).
    string FinesQuerySource,
    // HU #11357/#11362 (ADR-0043) — elegibilidad de documentos personalizados, desacoplada del canal.
    bool DocumentosPersonalizadosActivo,
    bool AvisosAprobacionActivos,
    bool AvisosRechazoActivos,
    DestinatariosNotificacionDto DestinatariosNotificacion,
    // HU #12250 (Feature #12249) — flags de módulos del dashboard.
    bool TramitesModuleEnabled,
    bool ComparendosModuleEnabled,
    bool ResolucionesModuleEnabled,
    // HU #13400 (Feature #13398) — generación automática de improntas por compañía. Solo la sirven
    // los endpoints de settings, reservados al SuperAdmin; ningún otro rol recibe este campo.
    bool GeneracionImprontas = true);

/// <summary>Checkboxes + correo extra de avisos de estado.</summary>
public sealed record DestinatariosNotificacionDto(
    bool Comprador,
    bool VendedorOPropietario,
    bool Radicador,
    string? ExtraEmail);
