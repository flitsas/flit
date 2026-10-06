using Flit.Api.Middleware;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Flit.Api.Telemetry;

/// <summary>
/// HU #13332 (Epic #13316): trazas y logs de un servicio hacia un colector OpenTelemetry (OTLP).
/// </summary>
/// <remarks>
/// <para>Se enciende solo si el ambiente define <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (por ejemplo
/// <c>http://observabilidad:4317</c>). Sin esa variable no se registra nada y el servicio queda exactamente como antes:
/// así ningún ambiente cambia hasta que el líder despliegue el colector.</para>
/// <para>Trazas: peticiones entrantes (sin los chequeos de salud) y llamadas salientes por HttpClient, que incluyen
/// las de gRPC; el contexto W3C (<c>traceparent</c>) viaja solo y une los tramos de todos los servicios en una traza.
/// Logs: los mismos de siempre, también hacia el colector, con sus alcances (entre ellos <c>CorrelationId</c>).</para>
/// <para>Si el colector está caído, el exportador descarta en segundo plano: las peticiones no esperan ni fallan.</para>
/// </remarks>
public static class FlitTelemetryExtensions
{
    public const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static WebApplicationBuilder AddFlitTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!IsEnabled(builder.Configuration))
            return builder;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
            o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
            o.AddOtlpExporter();
        });

        return builder;
    }

    /// <summary>Identificador de correlación por petición (ver <see cref="CorrelationIdMiddleware"/>). Va al inicio del pipeline.</summary>
    public static IApplicationBuilder UseFlitCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    internal static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[EndpointKey]);
}
