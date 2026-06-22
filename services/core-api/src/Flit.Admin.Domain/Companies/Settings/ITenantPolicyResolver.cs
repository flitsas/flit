namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Resuelve la política operativa <em>efectiva</em> para un trámite (AC4, RF03).
///
/// Regla de negocio: un trámite en curso (in-flight) conserva la política vigente
/// al momento de su creación (snapshot); los cambios posteriores en
/// <c>tenant_operational_policies</c> no lo afectan. Las nuevas radicaciones sí
/// usan la configuración live. La entidad EF de <c>tramites.procedure_instances</c>
/// no existe aún en esta HU, por lo que el snapshot se modela como contrato.
/// </summary>
public interface ITenantPolicyResolver
{
    /// <summary>
    /// Política efectiva de un trámite ya en curso: siempre el
    /// <paramref name="snapshotAtCreation"/>, ignorando <paramref name="liveSettings"/>.
    /// </summary>
    TenantSettings ResolveForInFlightProcedure(TenantSettings snapshotAtCreation, TenantSettings liveSettings);

    /// <summary>Política efectiva de una nueva radicación: la configuración live.</summary>
    TenantSettings ResolveForNewProcedure(TenantSettings liveSettings);
}
