using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Adaptador del puerto <see cref="IMatriculaInicialGate"/>: consulta el toggle
/// <c>SwitchesMatricula.AllowInitialRegistration</c> de la compañía vía el caso de uso del módulo
/// Admin. Vive en la API para no acoplar Tramites.Application con Admin.Application.
/// </summary>
internal sealed class MatriculaInicialGate(GetTenantSettingsHandler settingsHandler) : IMatriculaInicialGate
{
    public async Task<bool> IsAllowedAsync(Guid tenantId, CancellationToken ct = default)
    {
        var settings = await settingsHandler.HandleAsync(
            new GetTenantSettingsQuery { TenantId = tenantId }, ct);
        return settings is { SwitchesMatricula.AllowInitialRegistration: true };
    }
}

/// <summary>
/// Adaptador del puerto <see cref="ITransitOfficeCodeResolver"/>: resuelve el código del organismo
/// de tránsito contra el catálogo en memoria (<see cref="ITransitOfficeCatalog"/>).
/// </summary>
internal sealed class TransitOfficeCodeResolver(ITransitOfficeCatalog catalog) : ITransitOfficeCodeResolver
{
    public Guid? ResolveId(string code)
    {
        var entry = catalog.All.FirstOrDefault(
            e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
        return entry?.Id;
    }
}
