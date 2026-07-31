namespace Flit.Admin.Application.Companies.TransitOffices.UpdateTransitOfficeQuipuxSettings;

/// <summary>
/// Payload de la parametrización Quipux de una secretaría del catálogo
/// (PUT /api/v1/admin/transit-offices/{id}/quipux-settings, HU #10710). Campos en camelCase
/// vía las System.Text.Json Web defaults.
/// </summary>
/// <remarks>
/// Es un reemplazo completo (PUT): los cuatro campos describen el estado destino. Las
/// banderas son <c>bool?</c> para distinguir "no enviada" de <c>false</c> y devolver 422 en
/// vez de apagar una bandera por omisión.
/// </remarks>
/// <param name="DivipoCode">
/// Código DIVIPO que espera Quipux. <c>null</c> o cadena vacía = desconocido: la secretaría
/// deja de ser elegible para radicar. NO es un error — es el estado normal de las 311 aún no
/// integradas.
/// </param>
/// <param name="QuipuxRegistration">¿Se radican las matrículas de esta secretaría por Quipux? Requerido.</param>
/// <param name="QuipuxTransfer">¿Se radican los traspasos de esta secretaría por Quipux? Requerido.</param>
/// <param name="QuipuxOther">¿Se radican los otros trámites de esta secretaría por Quipux? Requerido.</param>
public sealed record UpdateTransitOfficeQuipuxSettingsRequest(
    string? DivipoCode,
    bool? QuipuxRegistration,
    bool? QuipuxTransfer,
    bool? QuipuxOther);
