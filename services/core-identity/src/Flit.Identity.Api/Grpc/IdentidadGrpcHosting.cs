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
            .RequireServiceToken<IdentidadGrpcService>(IdentidadGrpcService.Scope, ServiceAudiences.Plataforma);

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
        app.MapFlitGrpcPlatform(app.Environment, $"*:{grpcPort}");
    }
}
