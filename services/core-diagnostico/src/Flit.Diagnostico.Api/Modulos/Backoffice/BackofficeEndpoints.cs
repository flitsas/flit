using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Rutas <c>/backoffice/*</c> del backoffice FLIT (HU #13728, D-16, ADR-0073). Todo el grupo exige
/// <see cref="PoliticaBackoffice"/> (permiso <c>diagnostico.backoffice.ver</c> + <c>tenant_type=FLIT</c>) y la empresa
/// explícita (<see cref="EmpresaExplicita"/>, que además audita cada lectura). Una ruta inexistente bajo
/// <c>/backoffice/</c> también pasa por la política: 403 a quien no es backoffice FLIT, 404 a quien sí. Existen
/// <c>GET /backoffice/resumen?empresa=</c> (D0) y <c>GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato</c>
/// (<see cref="ContratoBackofficeEndpoints"/>, HU #13741) y <c>GET /backoffice/consultas/{consulta}/intentos</c>
/// (<see cref="IntentosBackofficeEndpoints"/>, HU #13737) y los lotes, la grilla y el historial de una compañía
/// (<see cref="DiagnosticosBackofficeEndpoints"/>, HU #13799); D3 suma aquí las suyas.
/// </summary>
public static class BackofficeEndpoints
{
    public const string Grupo = "/backoffice";
    public const string RutaResumen = "/resumen";

    internal static void Map(RouteGroupBuilder api)
    {
        var backoffice = api.MapGroup(Grupo).RequireAuthorization(PoliticaBackoffice.Nombre).ConEmpresaExplicita();
        backoffice.MapGet(RutaResumen, Resumen).WithName("ObtenerResumenEmpresaBackoffice");
        ContratoBackofficeEndpoints.Map(backoffice);
        IntentosBackofficeEndpoints.Map(backoffice);
        DiagnosticosBackofficeEndpoints.Map(backoffice);

        api.MapGroup(Grupo).RequireAuthorization(PoliticaBackoffice.Nombre).MapFallback(() => Results.NotFound());
    }

    /// <summary>Existencia de configuración y conteos de la empresa indicada (sin datos de sus vehículos ni de sus usuarios).</summary>
    private static async Task<IResult> Resumen(HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var configuracion = await db.DeLaEmpresa<ConfiguracionEmpresa>(empresa).AnyAsync(ct).ConfigureAwait(false);
        var diagnosticos = await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).CountAsync(ct).ConfigureAwait(false);
        var vehiculos = await db.DeLaEmpresa<VehiculoDiagnostico>(empresa).CountAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { empresa, configuracion, diagnosticos, vehiculos });
    }
}
