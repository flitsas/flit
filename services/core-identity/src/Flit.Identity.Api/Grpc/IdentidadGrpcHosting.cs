using System.Collections.Frozen;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// El gRPC de Identidad (HU #13334) es opt-in por <c>Identidad:GrpcPort</c>: sin esa clave core-identity arranca
/// EXACTAMENTE como antes (mismos endpoints y mismo binding). Con ella, escucha además en ese puerto solo HTTP/2 (h2c en
/// la red interna, sin publicar en la VPS ni pasar por nginx, contrato v1.3 §11) y el servicio solo responde en ese
/// puerto, nunca en el del REST que publica el gateway. Mismo patrón que <c>Ict:GrpcPort</c> en core-api.
/// </summary>
internal static class IdentidadGrpcHosting
{
    public const string PortKey = "Identidad:GrpcPort";

    public static void AddIdentidadGrpc(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetValue<int?>(PortKey) is not { } grpcPort)
            return;

        builder.Services.AddScoped<UsuariosEmpresaQuery>();
        // SDK de plataforma (HU #13337): errores del §10, grpc.health.v1 y validación del token de servicio y la empresa.
        builder.Services.AddFlitGrpcServer()
            .RequireServiceToken<IdentidadGrpcService>(IdentidadGrpcService.Scope, ServiceAudiences.Plataforma)
            // Global: responde por todas las empresas, con su propio scope y sin x-flit-tenant-id. HU #14694: el rpc del
            // alta en el proveedor va por una empresa y con su scope (ServiceTokenPorMetodoInterceptor).
            .AddServiceOptions<EmpresasGrpcService>(o => o.Interceptors.Add<ServiceTokenPorMetodoInterceptor>(
                EmpresasGrpcService.Scope, ServiceAudiences.Plataforma, false))
            // ADR-0092: API key del impuesto; solo svc-consultas, con x-flit-tenant-id. HU #14694: la copia para
            // svc-impuestos tiene su propio scope.
            .AddServiceOptions<CredencialesProductoGrpcService>(o => o.Interceptors.Add<ServiceTokenPorMetodoInterceptor>(
                CredencialesProductoGrpcService.Scope, ServiceAudiences.Plataforma, true))
            // HU #14694 (plan §1.2 R1): estado de aprovisionamiento; solo svc-impuestos, con x-flit-tenant-id.
            .RequireServiceToken<AprovisionamientoProductoGrpcService>(AprovisionamientoProductoGrpcService.Scope, ServiceAudiences.Plataforma);

        // h2c necesita un endpoint dedicado solo HTTP/2: Kestrel no mezcla HTTP/1.1 y HTTP/2 en texto plano en un mismo
        // puerto. Al declarar endpoints por código Kestrel ignora ASPNETCORE_URLS, así que se vuelven a declarar las
        // mismas URLs del REST (mismo host y puerto que hoy) y se añade el del gRPC.
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var restUrls = (context.Configuration[WebHostDefaults.ServerUrlsKey] ?? "http://localhost:4025")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var raw in restUrls)
            {
                var uri = new Uri(raw
                    .Replace("://+", "://0.0.0.0", StringComparison.Ordinal)
                    .Replace("://*", "://0.0.0.0", StringComparison.Ordinal));
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    options.ListenLocalhost(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
                else
                    options.ListenAnyIP(uri.Port, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
            }

            options.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
        });
    }

    public static void MapIdentidadGrpc(this WebApplication app)
    {
        if (app.Configuration.GetValue<int?>(PortKey) is not { } grpcPort)
            return;

        app.MapGrpcService<IdentidadGrpcService>().RequireHost($"*:{grpcPort}");
        app.MapGrpcService<EmpresasGrpcService>().RequireHost($"*:{grpcPort}");
        app.MapGrpcService<CredencialesProductoGrpcService>().RequireHost($"*:{grpcPort}");
        app.MapGrpcService<AprovisionamientoProductoGrpcService>().RequireHost($"*:{grpcPort}");
        app.MapFlitGrpcPlatform(app.Environment, $"*:{grpcPort}");
    }
}

/// <summary>
/// HU #14694 — como <see cref="PlatformServiceCallInterceptor"/>, pero con el scope (y la exigencia de empresa) propios de
/// los métodos de <see cref="MetodosConScopePropio"/>: un servicio gRPC que mezcla rpc de clientes distintos no abre uno con
/// el scope del otro. Lista cerrada, por nombre completo del método; los demás usan el scope del servicio.
/// </summary>
internal sealed class ServiceTokenPorMetodoInterceptor(string scope, string audience, bool requireTenant) : global::Grpc.Core.Interceptors.Interceptor
{
    /// <summary>Método completo → (scope, exige empresa).</summary>
    private static readonly FrozenDictionary<string, (string Scope, bool Empresa)> MetodosConScopePropio =
        new Dictionary<string, (string Scope, bool Empresa)>(StringComparer.Ordinal)
        {
            ["/flit.identidad.v1.CredencialesProductoService/ObtenerCredencialImpuestoParaCopia"] = (CredencialesProductoGrpcService.ScopeCopia, true),
            ["/flit.identidad.v1.EmpresasService/ObtenerDatosAltaProveedor"] = (EmpresasGrpcService.ScopeAlta, true),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>El scope y la exigencia de empresa de <paramref name="metodo"/>.</summary>
    internal static (string Scope, bool Empresa) Requisito(string metodo, string scopePorDefecto, bool empresaPorDefecto) =>
        MetodosConScopePropio.TryGetValue(metodo, out var propio) ? propio : (scopePorDefecto, empresaPorDefecto);

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, global::Grpc.Core.ServerCallContext context, global::Grpc.Core.UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (scopeDelMetodo, empresa) = Requisito(context.Method, scope, requireTenant);
        return new PlatformServiceCallInterceptor(scopeDelMetodo, audience, empresa).UnaryServerHandler(request, context, continuation);
    }
}
