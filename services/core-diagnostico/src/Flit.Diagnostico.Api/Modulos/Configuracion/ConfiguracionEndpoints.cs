using System.Text.Json;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// <c>GET/PUT /configuracion</c> de la compañía del token (HU #13741, plan §6.1, D-04 = b). La lectura trae los dos niveles
/// (el contractual en solo lectura, sin <c>redaccion_ia_suspendida</c>); la edición solo el operativo. El nivel contractual
/// de otra compañía lo edita el backoffice FLIT en <c>/backoffice/empresas/{empresa}/configuracion/contrato</c>
/// (<c>Backoffice.ContratoBackofficeEndpoints</c>). Ambas respuestas traen <c>fuentes_disponibles</c> (HU #13863).
/// </summary>
internal static class ConfiguracionEndpoints
{
    public const string Ruta = "/configuracion";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet(Ruta, Obtener).RequirePermiso(Permisos.ConfiguracionVer).WithName("ObtenerConfiguracionDiagnostico");
        api.MapPut(Ruta, Editar).RequirePermiso(Permisos.ConfiguracionEditar).WithName("EditarConfiguracionOperativa");
    }

    private static async Task<IResult> Obtener(ServicioConfiguracion servicio, Consultas.FuentesDisponibles fuentes, CancellationToken ct) =>
        Results.Ok(ConfiguracionDto.De(await servicio.VigenteAsync(ct).ConfigureAwait(false), fuentes.Todas()));

    private static async Task<IResult> Editar(JsonElement cuerpo, ServicioConfiguracion servicio, Consultas.FuentesDisponibles fuentes, CancellationToken ct) =>
        Results.Ok(ConfiguracionDto.De(await servicio.EditarOperativaAsync(cuerpo, ct).ConfigureAwait(false), fuentes.Todas()));
}
