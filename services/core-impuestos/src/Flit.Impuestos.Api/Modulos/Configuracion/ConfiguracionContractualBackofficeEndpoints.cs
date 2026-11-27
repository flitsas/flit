using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Configuracion;

/// <summary>
/// <c>PUT /backoffice/empresas/{tenantId}/configuracion-contractual</c> (plan §5.1, CO-01; permiso
/// <c>impuestos.configuracion.contrato.editar</c>, solo Super Admin): <c>{maxRegistrosLote, departamentosHabilitados[] | null}</c>
/// → 200 con la configuración completa. Va bajo <see cref="PoliticaBackoffice"/> y con la compañía explícita de la ruta
/// (<see cref="EmpresaExplicita"/>). 404 <c>EMPRESA_NO_ENCONTRADA</c> si la compañía no tiene Impuestos; 409
/// <c>APROVISIONAMIENTO_NO_LISTO</c> si no está en Listo o Listo sin webhook (AC-BE-77); 422 <c>TOPE_INVALIDO</c> o
/// <c>DEPARTAMENTO_NO_DISPONIBLE</c>.
/// </summary>
/// <example>
/// <code>
/// ConfiguracionContractualBackofficeEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class ConfiguracionContractualBackofficeEndpoints
{
    public const string Ruta = "/backoffice/empresas/{tenantId}/configuracion-contractual";
    public const string Recurso = "configuracion-contractual";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre)
            .MapPut(string.Empty, GuardarAsync).RequirePermiso(Permisos.ConfiguracionContratoEditar).ConEmpresaExplicita(Recurso).CuerpoAcotado()
            .WithName("GuardarConfiguracionContractual");
    }

    private static async Task<IResult> GuardarAsync(
        HttpContext http, ConfiguracionContractualEntrada entrada, ServicioConfiguracion servicio, UsuarioActual usuario, CancellationToken ct) =>
        Results.Ok(await servicio.GuardarContractualAsync(EmpresaExplicita.De(http), entrada, ConfiguracionEndpoints.Actor(usuario), ct).ConfigureAwait(false));
}
