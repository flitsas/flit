using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Lectura entre compañías de los reportes del backoffice (HU #14579, BE-4; SP-4, ADR-0088). No es un acceso genérico: un acceso por
/// cada tabla que leen los reportes, todos sin seguimiento y con <c>tenant_id IN (empresas)</c>, donde <c>empresas</c> son las
/// habilitadas de Identidad (B1) o la empresa validada de B4. Los reportes de <c>Modulos/Reportes/Catalogo</c> reciben estas consultas ya
/// acotadas y unen cada tabla por <c>tenant_id</c> además del id, así que una fila de una compañía nunca se cruza con la de otra.
/// </summary>
internal static class AccesosReportesEntreEmpresas
{
    public static FuentesReporte De(DiagnosticoDb db, IReadOnlyCollection<Guid> empresas)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(empresas);
        var ids = empresas.Distinct().ToList();
        return new FuentesReporte(
            db.Set<CabeceraDiagnostico>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<VehiculoDiagnostico>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<PersonaVehiculo>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<CeldaConsulta>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<IntentoConsulta>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<DictamenVehiculo>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<SaneamientoEntidad>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<RenglonSaneamiento>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<OperacionDerivacion>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)),
            db.Set<ItemDerivacion>().IgnoreQueryFilters().AsNoTracking().Where(x => ids.Contains(x.TenantId)));
    }
}
