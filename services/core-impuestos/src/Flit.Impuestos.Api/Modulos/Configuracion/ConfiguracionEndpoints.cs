using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Plataforma;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Configuracion;

/// <summary>
/// Configuración de la compañía (plan §5.1, D-CONFIG):
/// <list type="bullet">
///   <item><c>GET /configuracion</c> (<c>impuestos.configuracion.ver</c>): contractual (solo lectura), operativa y departamentos
///   disponibles. La compañía es la del token; el Super Admin la elige con <c>?empresa=&lt;tenantId&gt;</c> (obligatoria para él:
///   400 <c>EMPRESA_REQUERIDA</c>) y su lectura queda auditada. Otro usuario con <c>empresa</c> recibe 403
///   <c>EMPRESA_SOLO_SUPER_ADMIN</c> (regla de <see cref="EmpresaExplicita"/>, delta 8 de la 1.1.0).</item>
///   <item><c>PUT /configuracion</c> (<c>impuestos.configuracion.editar</c>): <c>{maxRegistrosLote, correosAviso[]}</c> de la
///   compañía del token o, para el Super Admin, de <c>?empresa</c> con la misma regla que el GET (delta 3 de la 1.1.0) → 200
///   con la configuración completa; 404 <c>EMPRESA_NO_ENCONTRADA</c> si la compañía elegida no tiene Impuestos (en el GET
///   también; delta 14), sin sembrar la fila; 422 <c>TOPE_SUPERA_CONTRACTUAL</c> (con <c>tope</c>), <c>TOPE_INVALIDO</c> o
///   <c>CORREO_INVALIDO</c>.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddConfiguracionImpuestos();
/// ConfiguracionEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class ConfiguracionEndpoints
{
    public const string Ruta = "/configuracion";
    public const string Recurso = "configuracion";

    public static IServiceCollection AddConfiguracionImpuestos(this IServiceCollection services)
    {
        services.AddScoped<ServicioConfiguracion>();
        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(Ruta, LeerAsync).RequirePermiso(Permisos.ConfiguracionVer).WithName("ObtenerConfiguracionImpuestos");
        api.MapPut(Ruta, GuardarAsync).RequirePermiso(Permisos.ConfiguracionEditar).CuerpoAcotado().WithName("GuardarConfiguracionOperativa");
    }

    private static async Task<IResult> LeerAsync(
        HttpContext http, ServicioConfiguracion servicio, UsuarioActual usuario, AuditoriaLecturaBackoffice auditoria, CancellationToken ct)
    {
        var empresa = await EmpresaDeLecturaAsync(http.Request.Query[EmpresaExplicita.Parametro], usuario, servicio, auditoria, ct).ConfigureAwait(false);
        return Results.Ok(await servicio.LeerAsync(empresa, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> GuardarAsync(
        HttpContext http, ConfiguracionOperativaEntrada entrada, ServicioConfiguracion servicio, UsuarioActual usuario, CancellationToken ct)
    {
        var empresa = usuario.EmpresaPropia();
        if (EmpresaExplicita.Opcional(usuario.EsSuperAdmin, http.Request.Query[EmpresaExplicita.Parametro]) is { } elegida)
        {
            await servicio.ExigirConImpuestosAsync(elegida, ct).ConfigureAwait(false);
            empresa = elegida;
        }

        return Results.Ok(await servicio.GuardarOperativaAsync(empresa, entrada, Actor(usuario), ct).ConfigureAwait(false));
    }

    /// <summary>
    /// La compañía de una lectura con <c>empresa</c> opcional (contrato: parámetro <c>Empresa</c>; regla de
    /// <see cref="EmpresaExplicita.Opcional"/>): sin él, la del token (el Super Admin debe elegirla); con él, solo el Super
    /// Admin, con un único uuid válido y de una compañía con Impuestos (404 <c>EMPRESA_NO_ENCONTRADA</c>, delta 14), y la
    /// lectura se audita antes de leer.
    /// </summary>
    internal static async Task<Guid> EmpresaDeLecturaAsync(
        StringValues consulta, UsuarioActual usuario, ServicioConfiguracion servicio, AuditoriaLecturaBackoffice auditoria, CancellationToken ct)
    {
        if (EmpresaExplicita.Opcional(usuario.EsSuperAdmin, consulta) is not { } empresa)
            return usuario.EmpresaPropia();
        await servicio.ExigirConImpuestosAsync(empresa, ct).ConfigureAwait(false);
        await auditoria.RegistrarAsync(empresa, Recurso, ct).ConfigureAwait(false);
        return empresa;
    }

    internal static Guid? Actor(UsuarioActual usuario) => usuario.Id == Guid.Empty ? null : usuario.Id;
}
