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
/// la política de configuración de <see cref="PoliticaBackoffice"/> (SuperAdmin de cualquier compañía, o FLIT con
/// <c>diagnostico.backoffice.ver</c>; ajustes 2026-11-11, punto 1; el override y el llenado manual exigen además la de
/// operación FLIT) y la empresa explícita (<see cref="EmpresaExplicita"/>, que además audita cada lectura). Una ruta inexistente bajo
/// <c>/backoffice/</c> también pasa por la política: 403 a quien no la cumple, 404 a quien sí. Existen
/// <c>GET /backoffice/resumen?empresa=</c> (D0) y <c>GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato</c>
/// (<see cref="ContratoBackofficeEndpoints"/>, HU #13741) y <c>GET /backoffice/consultas/{consulta}/intentos</c>
/// (<see cref="IntentosBackofficeEndpoints"/>, HU #13737) y los lotes, la grilla y el historial de una compañía
/// (<see cref="DiagnosticosBackofficeEndpoints"/>, HU #13799) y el llenado manual de una celda en ERROR
/// (<see cref="LlenadoManualEndpoints"/>, HU #13800), el dictamen vigente y su reemplazo
/// (<see cref="OverrideDictamenEndpoints"/>, corte 7) y la lista de empresas con Diagnóstico para el editor de contrato
/// (<c>GET /backoffice/empresas</c>, <see cref="EmpresasBackofficeEndpoints"/>, HU #13741) y el catálogo global de
/// fuentes obligatorias (<c>GET/PUT /backoffice/fuentes-obligatorias</c>, <see cref="FuentesObligatoriasBackofficeEndpoints"/>,
/// ADR-0078). Esas dos no piden empresa explícita.
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
        LlenadoManualEndpoints.Map(backoffice);
        OverrideDictamenEndpoints.Map(backoffice);

        // HU #13741 (escenario 15): la lista de empresas de la plataforma no lee datos de ninguna compañía, así que no pide
        // empresa explícita ni se audita como lectura entre compañías; la política del backoffice sí aplica.
        EmpresasBackofficeEndpoints.Map(api.MapGroup(Grupo).RequireAuthorization(PoliticaBackoffice.Nombre));

        // ADR-0078: el catálogo de fuentes obligatorias es de FLIT, igual que la lista de empresas. La política aplica;
        // no pide ?empresa=, no audita lecturas_backoffice y no abre AutorizarEscrituraDe.
        FuentesObligatoriasBackofficeEndpoints.Map(api.MapGroup(Grupo).RequireAuthorization(PoliticaBackoffice.Nombre));

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
