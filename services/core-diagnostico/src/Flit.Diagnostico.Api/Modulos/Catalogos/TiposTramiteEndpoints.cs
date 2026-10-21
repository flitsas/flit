using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Platform.Sdk.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// <c>GET /tipos-tramite</c> (HU #13742, plan §6.1): los tipos de trámite de la etapa publicados por Trámites, para el modo
/// Con Análisis IA. Permiso <c>diagnostico.diagnosticos.crear</c>. Catálogo global: no depende de la compañía. Si Trámites no
/// responde y no hay copia vigente, 503 <c>CATALOGO_NO_DISPONIBLE</c> con <c>Retry-After: 30</c>.
/// </summary>
internal static class TiposTramiteEndpoints
{
    public const string Ruta = "/tipos-tramite";

    /// <summary>Dirección del gRPC de core-api (en el compose, <c>http://core-api:8082</c>).</summary>
    public const string ClaveDireccion = "Diagnostico:Tramites:Address";

    /// <summary>
    /// Registra el cliente gRPC con el token de svc-diagnostico para los tipos de trámite y la réplica de organismos (HU
    /// #13733); sin dirección, los dos catálogos quedan no conectados y la réplica no se sincroniza.
    /// </summary>
    public static IServiceCollection AddCatalogoTiposTramite(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddMemoryCache();
        if (configuration[ClaveDireccion] is { Length: > 0 } tramites)
        {
            services.AddFlitGrpcClient<Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient>(
                configuration, new Uri(tramites), "platform.tramites.catalogos", o => o.Deadline = CatalogoTiposTramiteGrpc.PlazoPorLlamada);
            services.TryAddScoped<ICatalogoTiposTramite, CatalogoTiposTramiteGrpc>();
            // HU #13733: réplica de organismos por el mismo cliente gRPC, al arrancar y cada día.
            services.TryAddScoped<ICatalogoOrganismosPlataforma, CatalogoOrganismosGrpc>();
            services.AddHostedService<TrabajadorSincronizacionOrganismos>();
        }
        else
        {
            services.TryAddSingleton<ICatalogoTiposTramite, CatalogoTiposTramiteNoConectado>();
            services.TryAddSingleton<ICatalogoOrganismosPlataforma, CatalogoOrganismosNoConectado>();
        }

        services.AddScoped<SincronizacionOrganismos>();
        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet(Ruta, Listar).RequirePermiso(Permisos.DiagnosticosCrear).WithName("ListarTiposTramiteDiagnostico");
    }

    private static async Task<IResult> Listar(ICatalogoTiposTramite catalogo, CancellationToken ct)
    {
        IReadOnlyList<TipoTramite> tipos;
        try
        {
            tipos = await catalogo.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (CatalogoTiposTramiteNoDisponibleException ex)
        {
            throw ReglaException.CatalogoNoDisponible(ex);
        }

        return Results.Ok(new TiposTramiteDto([.. tipos.Select(t => new TipoTramiteDto(t.Codigo, t.Nombre, t.Familia, [.. t.FuentesObligatorias.Select(CodigoFuente)]))]));
    }

    private static string CodigoFuente(Fuente fuente) => CodigosDeEnum.Codigo(fuente);
}

internal sealed record TiposTramiteDto(IReadOnlyList<TipoTramiteDto> Tipos);

internal sealed record TipoTramiteDto(string Codigo, string Nombre, string Familia, IReadOnlyList<string> FuentesObligatorias);
