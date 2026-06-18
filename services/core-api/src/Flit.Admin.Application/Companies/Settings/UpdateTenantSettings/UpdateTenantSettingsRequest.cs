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
    IReadOnlyList<string>? MetodosRecaudo);
