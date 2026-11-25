using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Límites de solicitudes de core-impuestos. Hoy una política con nombre: <see cref="Avisos"/>, el receptor anónimo del webhook
/// (<c>POST /avisos/{token}</c>), con ventana fija de un minuto por IP (plan §1.5: más de 60 por minuto ⇒ 429). Por encima del límite
/// responde 429 <see cref="CodigoDemasiadasSolicitudes"/> con <c>Retry-After</c> y deja un aviso en el log con la política (sin la IP
/// completa ni el token). Los límites se leen de <see cref="Seccion"/>; un valor menor que 1 impide arrancar.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddLimitesSolicitudes(builder.Configuration);
/// app.UseRateLimiter();
/// api.MapPost("/avisos/{token}", Recibir).RequireRateLimiting(LimitesSolicitudes.Avisos);
/// </code>
/// </example>
internal static partial class LimitesSolicitudes
{
    public const string Avisos = "impuestos-avisos";
    public const string CodigoDemasiadasSolicitudes = "DEMASIADAS_SOLICITUDES";
    public const string Seccion = "Impuestos:LimitesSolicitudes";

    public static IServiceCollection AddLimitesSolicitudes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = configuration.GetSection(Seccion).Get<OpcionesLimitesSolicitudes>() ?? new OpcionesLimitesSolicitudes();
        o.Validar();
        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limitador.AddPolicy(Avisos, ctx => RateLimitPartition.GetFixedWindowLimiter(
                $"{Avisos}:{ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida"}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = o.AvisosPorIpPorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
            limitador.OnRejected = RechazarAsync;
        });
        return services;
    }

    private static async ValueTask RechazarAsync(OnRejectedContext contexto, CancellationToken ct)
    {
        var http = contexto.HttpContext;
        var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera) ? Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds)) : 60;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        var politica = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
        Rechazada(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LimitesSolicitudes).FullName!), politica, segundos);
        await http.Response.WriteAsJsonAsync(
            new { code = CodigoDemasiadasSolicitudes, error = $"Demasiadas solicitudes seguidas. Espera {segundos} segundos y vuelve a intentarlo." }, ct)
            .ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Límite de solicitudes {Politica} superado; reintento en {Segundos} s")]
    private static partial void Rechazada(ILogger logger, string politica, int segundos);
}

/// <summary>Límites de <see cref="LimitesSolicitudes"/> (sección <c>Impuestos:LimitesSolicitudes</c>).</summary>
internal sealed class OpcionesLimitesSolicitudes
{
    /// <summary>Avisos del webhook por IP y minuto (plan §1.5).</summary>
    public int AvisosPorIpPorMinuto { get; set; } = 60;

    public void Validar()
    {
        if (AvisosPorIpPorMinuto < 1)
            throw new InvalidOperationException($"{LimitesSolicitudes.Seccion}: todos los límites deben ser al menos 1.");
    }
}
