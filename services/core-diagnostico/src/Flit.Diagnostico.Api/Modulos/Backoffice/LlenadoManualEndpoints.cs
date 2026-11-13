using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Llenado manual de una celda en ERROR (HU #13800, F06-H3, plan §6.1 fila manual). Cuelga del grupo <c>/backoffice</c>
/// (<see cref="PoliticaBackoffice"/>; empresa explícita en <c>?empresa=</c>) y exige además la política de operación FLIT
/// (<see cref="PoliticaBackoffice.NombreOperacion"/>: un SuperAdmin de otra compañía recibe 403, ajustes 2026-11-11 punto 1)
/// y <c>diagnostico.consultas.llenar</c> (rol <c>backoffice_diagnostico</c>): el admin y el usuario
/// de la compañía reciben 403. Va bajo <c>/backoffice/*</c> y no en <c>/consultas/{cid}/manual</c> porque la escritura entre
/// compañías solo vive en este módulo (<see cref="EscrituraEntreEmpresas"/>, ADR-0073).
/// <list type="bullet">
/// <item><c>GET /backoffice/consultas/{consulta}/manual?empresa=</c>: el formulario (<see cref="FormularioManualDto"/>): versión,
/// si se puede llenar y las personas en error con su <c>persona_id</c> y el documento enmascarado. La lectura queda auditada en
/// <c>lecturas_backoffice</c>.</item>
/// <item><c>PUT /backoffice/consultas/{consulta}/manual?empresa=</c>: el llenado (<see cref="LlenadoManual"/>); la auditoría es el
/// intento MANUAL con el usuario.</item>
/// </list>
/// Celda de otra compañía (o que no existe): 404 <c>NO_ENCONTRADO</c>.
/// </summary>
internal static class LlenadoManualEndpoints
{
    public const string Ruta = "/consultas/{consulta:guid}/manual";

    public static void Map(RouteGroupBuilder backoffice)
    {
        // HU #14563 M2: la lectura de la celda trae personas (enmascaradas) y la referencia del avalúo; no se guarda en caché.
        backoffice.MapGet(Ruta, Formulario).RequiereOperacionFlit().RequirePermiso(Permisos.ConsultasLlenar).SinCacheEnRespuesta()
            .WithName("ObtenerFormularioLlenadoManualBackoffice");
        backoffice.MapPut(Ruta, Llenar).RequiereOperacionFlit().RequirePermiso(Permisos.ConsultasLlenar).WithName("LlenarConsultaManualBackoffice");
    }

    public static IServiceCollection AddLlenadoManual(this IServiceCollection services)
    {
        services.AddScoped<LlenadoManual>();
        return services;
    }

    private static async Task<IResult> Formulario(Guid consulta, HttpContext http, LlenadoManual llenado, CancellationToken ct) =>
        Results.Ok(await llenado.FormularioAsync(EmpresaExplicita.De(http), consulta, ct).ConfigureAwait(false));

    private static async Task<IResult> Llenar(Guid consulta, HttpContext http, JsonElement cuerpo, LlenadoManual llenado, CancellationToken ct) =>
        Results.Ok(await llenado.LlenarAsync(EmpresaExplicita.De(http), consulta, cuerpo, ct).ConfigureAwait(false));
}
