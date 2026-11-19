using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Dominio activo del portal de un organismo (Épica #14297, Feature #14380). <c>Activo = false</c> es la única respuesta ante un host
/// pendiente, fallido, retirado, reservado o desconocido: nunca revela por qué.
/// </summary>
public sealed record DominioPuntoRespuesta(bool Activo, string? Codigo, string? Host)
{
    public static readonly DominioPuntoRespuesta Ninguno = new(false, null, null);
}

/// <summary>Lectura del dominio contra la fachada de core-api. El BFF no lee las tablas y no guarda nada en caché.</summary>
public interface IDominioPunto
{
    Task<DominioPuntoRespuesta> PorHostAsync(string host, CancellationToken ct);

    Task<DominioPuntoRespuesta> PorCodigoAsync(string codigo, CancellationToken ct);
}

internal sealed class DominioPuntoNoConfigurado : IDominioPunto
{
    public Task<DominioPuntoRespuesta> PorHostAsync(string host, CancellationToken ct) => Task.FromResult(DominioPuntoRespuesta.Ninguno);

    public Task<DominioPuntoRespuesta> PorCodigoAsync(string codigo, CancellationToken ct) => Task.FromResult(DominioPuntoRespuesta.Ninguno);
}

internal sealed class DominioPuntoGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IDominioPunto
{
    private static Metadata Empresa() =>
        new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public Task<DominioPuntoRespuesta> PorHostAsync(string host, CancellationToken ct) =>
        LlamarAsync(new ObtenerDominioPuntoRequest { Host = host }, ct);

    public Task<DominioPuntoRespuesta> PorCodigoAsync(string codigo, CancellationToken ct) =>
        LlamarAsync(new ObtenerDominioPuntoRequest { Codigo = codigo }, ct);

    private async Task<DominioPuntoRespuesta> LlamarAsync(ObtenerDominioPuntoRequest request, CancellationToken ct)
    {
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerDominioPuntoAsync(request, Empresa(), cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        return r.Activo && !string.IsNullOrWhiteSpace(r.Codigo) && !string.IsNullOrWhiteSpace(r.Host)
            ? new DominioPuntoRespuesta(true, r.Codigo, r.Host)
            : DominioPuntoRespuesta.Ninguno;
    }
}

/// <summary>
/// HU #14383 — <c>GET /api/v1/persona-natural/punto/dominio/{host}</c> devuelve el código del organismo solo si ese dominio está
/// activo (la raíz del dominio pinta el punto de ese organismo, sin escribir código), y
/// <c>GET /api/v1/persona-natural/punto/dominio-activo/{codigo}</c> el dominio activo de un organismo (enlace, QR y enlaces de
/// correo lo usan). Ambos responden siempre 200, sin caché (<c>Cache-Control: no-store</c>): un dominio recién activado o retirado se
/// ve en la siguiente lectura. Ante una falla responden «sin dominio» y el portal sigue en el enlace del punto.
/// </summary>
public static class DominioPuntoEndpoints
{
    private const int HostMaxLargo = 253;
    private const int CodigoMaxLargo = 20;

    public static IServiceCollection AddDominioPunto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IDominioPunto>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente
                ? new DominioPuntoGrpc(cliente)
                : new DominioPuntoNoConfigurado());
        return services;
    }

    public static IEndpointRouteBuilder MapDominioPuntoEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var grupo = app.MapGroup(SesionEndpoints.Base).AllowAnonymous().RequireRateLimiting(AbusoEndpoints.PoliticaPorIp);

        // Contrato del frontend: { codigo } solo con dominio activo; 404 en cualquier otro caso (sin revelar el motivo).
        grupo.MapGet("/punto/dominio/{host}", async (string host, IDominioPunto dominio, HttpContext http, ILogger<IDominioPunto> log, CancellationToken ct) =>
        {
            var r = await ResolverAsync(host, HostMaxLargo, dominio.PorHostAsync, http, log, ct).ConfigureAwait(false);
            return r.Activo ? Results.Ok(new { codigo = r.Codigo }) : Results.NotFound();
        });

        grupo.MapGet("/punto/dominio-activo/{codigo}", async (string codigo, IDominioPunto dominio, HttpContext http, ILogger<IDominioPunto> log, CancellationToken ct) =>
            Results.Ok(await ResolverAsync(codigo, CodigoMaxLargo, dominio.PorCodigoAsync, http, log, ct).ConfigureAwait(false)));

        return app;
    }

    private static async Task<DominioPuntoRespuesta> ResolverAsync(
        string? valor, int largoMaximo, Func<string, CancellationToken, Task<DominioPuntoRespuesta>> consulta, HttpContext http, ILogger log,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var buscado = (valor ?? string.Empty).Trim();
        if (buscado.Length == 0 || buscado.Length > largoMaximo)
            return DominioPuntoRespuesta.Ninguno;
        try
        {
            return await consulta(buscado, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
#pragma warning disable CA1848 // Mensaje fijo, solo en falla.
            log.LogWarning(ex, "No se pudo leer el dominio del portal; se responde sin dominio.");
#pragma warning restore CA1848
            return DominioPuntoRespuesta.Ninguno;
        }
    }
}
