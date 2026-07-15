namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Payload del PUT de configuración operativa (RF03/RF07–RF10). Refleja el JSON
/// del endpoint. Los enums llegan como string (formato wire) y se validan en el
/// handler (AC2).
/// </summary>
public sealed record UpdateTenantSettingsRequest(
    SwitchesMatricula? SwitchesMatricula,
    bool BaulFirmasActivo,
    string? EnrutamientoSMTP,
    string? NotificationTarget,
    IReadOnlyList<string>? MetodosRecaudo,
    // HU #10478 — opcionales: si llegan null se conserva el valor previo (update parcial).
    int? RuntFailoverTimeoutMs = null,
    IReadOnlyDictionary<string, ConsultationProviderChoice>? ConsultationProviderConfig = null,
    // Feature #10587 — preasignación de placa por compañía (opcional: default false = retrocompatible).
    bool PreasignacionPlacaActiva = false,
    // Feature #10707 — proveedores de avalúo (opcional: si llega null se conserva el valor previo).
    AvaluoProviderConfigDto? AvaluoProviderConfig = null,
    // FEATURE 02 — fuente de comparendos (internal | external); null conserva el valor previo.
    string? FinesQuerySource = null);
