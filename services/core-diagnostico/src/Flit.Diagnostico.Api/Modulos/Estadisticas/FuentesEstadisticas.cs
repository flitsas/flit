using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Tablas que lee la calculadora, ya acotadas por quien llama (HU #14579, BE-1; SP-4, SP-5, SP-6; patrón <c>EscritorSaneamiento</c>). La
/// calculadora nunca abre <c>IgnoreQueryFilters</c>: la vista de compañía las arma aquí con el filtro de tenant normal y la visibilidad
/// del lote (<see cref="DeLaCompaniaAsync"/>); la del backoffice, en <c>Modulos/Backoffice/AccesosEstadisticasEntreEmpresas</c>, con
/// <c>tenant_id IN (empresas habilitadas)</c>. <see cref="Cabeceras"/> son los lotes visibles (de cualquier fecha): la cohorte la
/// recorta la calculadora y los saneamientos y derivaciones se cuentan solo si su lote es visible. Toda unión compara también
/// <c>tenant_id</c>.
/// </summary>
internal sealed record FuentesEstadisticas(
    IQueryable<CabeceraDiagnostico> Cabeceras,
    IQueryable<VehiculoDiagnostico> Vehiculos,
    IQueryable<CeldaConsulta> Celdas,
    IQueryable<IntentoConsulta> Intentos,
    IQueryable<DictamenVehiculo> Dictamenes,
    IQueryable<SaneamientoEntidad> Saneamientos,
    IQueryable<OperacionDerivacion> Operaciones,
    IQueryable<ItemDerivacion> Items)
{
    /// <summary>
    /// Vista de compañía (E1): <c>db.Set&lt;T&gt;()</c> con el filtro de tenant del token (el SuperAdmin también queda en la suya, SP-5)
    /// y los lotes que el usuario puede ver según <see cref="AccesoLote.VisiblesAsync"/> (PROPIOS, SP-6/O9).
    /// </summary>
    public static async Task<FuentesEstadisticas> DeLaCompaniaAsync(DiagnosticoDb db, UsuarioActual usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var cabeceras = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>().AsNoTracking(), db, usuario, ct).ConfigureAwait(false);
        return new FuentesEstadisticas(
            cabeceras,
            db.Set<VehiculoDiagnostico>().AsNoTracking(),
            db.Set<CeldaConsulta>().AsNoTracking(),
            db.Set<IntentoConsulta>().AsNoTracking(),
            db.Set<DictamenVehiculo>().AsNoTracking(),
            db.Set<SaneamientoEntidad>().AsNoTracking(),
            db.Set<OperacionDerivacion>().AsNoTracking(),
            db.Set<ItemDerivacion>().AsNoTracking());
    }
}
