using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Catalogos;

/// <summary>Un valor de un catálogo de vehículo. <c>Codigo</c> es el valor que se guarda en el campo (el mismo texto que guarda FLIT 2).</summary>
public sealed record ItemCatalogoDto(string Codigo, string Etiqueta);

/// <summary>Un organismo donde se puede trasladar una cuenta (cualquier organismo operable en FLIT 2).</summary>
public sealed record OrganismoDestinoDto(string Id, string Nombre, string Municipio, string Departamento);

/// <summary>
/// Catálogos que el portal le pide a core-api (HU 6 y 10): colores, combustibles y carrocerías del asistente de FLIT 2, y los
/// organismos de destino de un traslado de cuenta. Interfaz propia (hermana de <see cref="IFachadaTramites"/>) para no tocar las
/// fachadas falsas de las pruebas existentes.
/// </summary>
public interface IFachadaCatalogosVehiculo
{
    Task<IReadOnlyList<ItemCatalogoDto>> CatalogoAsync(string catalogo, string? texto, string? clase, int limite, CancellationToken ct);

    Task<IReadOnlyList<OrganismoDestinoDto>> DestinosAsync(string? texto, string? id, int limite, CancellationToken ct);
}

internal sealed class FachadaCatalogosVehiculoNoConfigurada : IFachadaCatalogosVehiculo
{
    private static FachadaException NoDisponible() => new("servicio_no_disponible", "El catálogo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);

    public Task<IReadOnlyList<ItemCatalogoDto>> CatalogoAsync(string catalogo, string? texto, string? clase, int limite, CancellationToken ct) => throw NoDisponible();

    public Task<IReadOnlyList<OrganismoDestinoDto>> DestinosAsync(string? texto, string? id, int limite, CancellationToken ct) => throw NoDisponible();
}

/// <summary>Cliente gRPC de <c>ListarCatalogoVehiculo</c> y <c>ListarOrganismosDestino</c> con el token de servicio y la empresa de Persona Natural.</summary>
internal sealed class FachadaCatalogosVehiculoGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaCatalogosVehiculo
{
    private static Metadata Empresa() =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public async Task<IReadOnlyList<ItemCatalogoDto>> CatalogoAsync(string catalogo, string? texto, string? clase, int limite, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ListarCatalogoVehiculoAsync(
            new ListarCatalogoVehiculoRequest { Catalogo = catalogo, Texto = texto ?? string.Empty, Clase = clase ?? string.Empty, Limite = limite },
            Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return [.. r.Items.Select(i => new ItemCatalogoDto(i.Codigo, i.Etiqueta))];
    }

    public async Task<IReadOnlyList<OrganismoDestinoDto>> DestinosAsync(string? texto, string? id, int limite, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ListarOrganismosDestinoAsync(
            new ListarOrganismosDestinoRequest { Texto = texto ?? string.Empty, Id = id ?? string.Empty, Limite = limite },
            Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return [.. r.Organismos.Select(o => new OrganismoDestinoDto(o.Id, o.Nombre, o.Municipio, o.Departamento))];
    }
}

/// <summary>
/// Catálogos de vehículo y organismos de destino para el wizard (web y punto): lectura pública, con el límite por IP y sin
/// captcha (igual que el catálogo de tipos). Nombre de catálogo validado contra una lista cerrada; el texto de búsqueda acotado.
/// Cachea unos segundos las consultas sin texto, que son las que se repiten al abrir el selector.
/// </summary>
public sealed class CatalogosVehiculoService(IFachadaCatalogosVehiculo fachada, IMemoryCache cache)
{
    public static readonly IReadOnlySet<string> Catalogos = new HashSet<string>(StringComparer.Ordinal) { "colores", "combustibles" };

    public const int MaxTexto = 80;
    public const int LimiteCatalogo = 50;
    public const int LimiteDestinos = 20;
    public const int LimiteMaximoDestinos = 50;

    public Task<IReadOnlyList<ItemCatalogoDto>> CatalogoAsync(string catalogo, string? texto, string? clase, CancellationToken ct)
    {
        var t = Limpiar(texto);
        var c = Limpiar(clase);
        if (t is not null)
            return fachada.CatalogoAsync(catalogo, t, c, LimiteCatalogo, ct);
        return cache.GetOrCreateAsync($"pn:cat:{catalogo}:{c?.ToUpperInvariant()}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await fachada.CatalogoAsync(catalogo, null, c, LimiteCatalogo, ct).ConfigureAwait(false);
        })!;
    }

    public Task<IReadOnlyList<OrganismoDestinoDto>> DestinosAsync(string? texto, int? limite, CancellationToken ct) =>
        fachada.DestinosAsync(Limpiar(texto), null, Math.Clamp(limite ?? LimiteDestinos, 1, LimiteMaximoDestinos), ct);

    /// <summary>¿El organismo existe y es operable en FLIT 2? (validación del destino que el ciudadano eligió).</summary>
    public async Task<bool> DestinoValidoAsync(Guid id, CancellationToken ct) =>
        (await fachada.DestinosAsync(null, id.ToString(), 1, ct).ConfigureAwait(false)).Count > 0;

    private static string? Limpiar(string? valor)
    {
        var v = valor?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > MaxTexto ? v[..MaxTexto] : v;
    }
}

public static class CatalogosVehiculoEndpoints
{
    public static IServiceCollection AddCatalogosVehiculoPortal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMemoryCache();
        services.TryAddSingleton<IFachadaCatalogosVehiculo>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new FachadaCatalogosVehiculoGrpc(cliente)
                : new FachadaCatalogosVehiculoNoConfigurada());
        services.TryAddSingleton<CatalogosVehiculoService>();
        return services;
    }

    /// <summary>
    /// <c>GET /catalogos/{colores|combustibles|carrocerias}?texto=&amp;clase=</c> → <c>{codigo, etiqueta}[]</c>;
    /// <c>GET /organismos-destino?texto=&amp;limite=</c> → <c>{id, nombre, municipio, departamento}[]</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapCatalogosVehiculoEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        grupo.MapGet("/catalogos/{catalogo}", async (string catalogo, string? texto, string? clase, CatalogosVehiculoService s, CancellationToken ct) =>
        {
            if (!CatalogosVehiculoService.Catalogos.Contains(catalogo))
                return SesionEndpoints.Problema(StatusCodes.Status404NotFound, "No encontrado", "Ese catálogo no existe.", "catalogo_desconocido");
            try
            {
                return Results.Ok(await s.CatalogoAsync(catalogo, texto, clase, ct).ConfigureAwait(false));
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        grupo.MapGet("/organismos-destino", async (string? texto, int? limite, CatalogosVehiculoService s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await s.DestinosAsync(texto, limite, ct).ConfigureAwait(false));
            }
            catch (FachadaException ex)
            {
                return SesionEndpoints.Problema(ex.EstadoHttp, "No disponible", ex.Message, ex.Codigo);
            }
        });

        return app;
    }
}
