namespace Flit.Admin.Application.Companies.Settings.GetActiveModules;

/// <summary>
/// Los 3 flags de módulos activos del dashboard (HU #12251, Feature #12249). Serializado en
/// camelCase. DTO deliberadamente MÍNIMO — separado de
/// <see cref="TenantSettingsResponse"/> para no exponer al usuario no-admin el resto de la
/// configuración operativa del tenant (payment methods, RUNT, avalúo, etc.), consultable solo
/// vía GET /api/v1/admin/companies/{tenantId}/settings (AdminCompanyPolicy).
/// </summary>
public sealed record ActiveModulesResponse(
    bool TramitesModuleEnabled,
    bool ComparendosModuleEnabled,
    bool ResolucionesModuleEnabled);
