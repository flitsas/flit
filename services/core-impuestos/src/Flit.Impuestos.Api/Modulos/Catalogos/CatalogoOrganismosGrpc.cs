using Flit.Platform.Sdk.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Modulos.Catalogos;

/// <summary>
/// Catálogo de organismos por el gRPC de catálogos de core-api (<c>flit.tramites.v1.CatalogosService.ListarOrganismosTransito</c>,
/// ADR-0070; contrato público <c>Flit.Tramites.Grpc.Contracts</c>, cliente <c>svc-impuestos</c> con el scope
/// <c>platform.tramites.catalogos</c>). Descarta los organismos sin código y recorta los textos; un texto vacío queda null.
/// </summary>
/// <example>
/// <code>
/// var organismos = await new CatalogoOrganismosGrpc(cliente).ListarAsync(ct);
/// </code>
/// </example>
public sealed class CatalogoOrganismosGrpc(Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient cliente) : ICatalogoOrganismosPlataforma
{
    /// <summary>Plazo de la llamada: el catálogo completo en una respuesta.</summary>
    public static readonly TimeSpan PlazoPorLlamada = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct)
    {
        var r = await cliente.ListarOrganismosTransitoAsync(new Flit.Tramites.Grpc.V1.ListarOrganismosTransitoRequest(), cancellationToken: ct)
            .ConfigureAwait(false);
        static string? Texto(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return [.. r.Organismos.Where(o => !string.IsNullOrWhiteSpace(o.Codigo))
            .Select(o => new OrganismoPlataforma(o.Codigo.Trim(), o.Nombre.Trim(), Texto(o.DepartamentoCodigo), Texto(o.Departamento), Texto(o.Municipio)))];
    }
}

/// <summary>
/// Registro de los catálogos de Impuestos (M08): réplica de organismos, homologación de la secretaría y caché de <c>/fuentes</c>.
/// Con <see cref="ClaveDireccion"/> se registra el cliente gRPC de catálogos con el token de <c>svc-impuestos</c> y el trabajador
/// que sincroniza la réplica al arrancar y cada día; sin ella, la réplica no se sincroniza. Un registro previo de
/// <see cref="ICatalogoOrganismosPlataforma"/> (pruebas) se respeta.
/// </summary>
/// <example>
/// <code>
/// Impuestos__Tramites__Address=http://core-api:8082
/// builder.Services.AddCatalogosImpuestos(builder.Configuration);
/// </code>
/// </example>
public static class CatalogosImpuestosExtensions
{
    /// <summary>Dirección del gRPC de core-api (catálogos de la plataforma).</summary>
    public const string ClaveDireccion = "Impuestos:Tramites:Address";

    public const string ScopeCatalogos = "platform.tramites.catalogos";

    public static IServiceCollection AddCatalogosImpuestos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[ClaveDireccion] is { Length: > 0 } tramites)
        {
            services.AddFlitGrpcClient<Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient>(
                configuration, new Uri(tramites), ScopeCatalogos, o => o.Deadline = CatalogoOrganismosGrpc.PlazoPorLlamada);
            services.TryAddScoped<ICatalogoOrganismosPlataforma, CatalogoOrganismosGrpc>();
            services.AddHostedService<TrabajadorSincronizacionOrganismos>();
        }
        else
        {
            services.TryAddSingleton<ICatalogoOrganismosPlataforma, CatalogoOrganismosNoConectado>();
        }

        services.AddScoped<SincronizacionOrganismos>();
        services.AddScoped<HomologacionSecretaria>();
        services.AddScoped<SincronizacionFuentes>();
        return services;
    }
}
