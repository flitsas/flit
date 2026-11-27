using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Límites de solicitudes de core-impuestos. Hoy una política con nombre: <see cref="Avisos"/>, el receptor anónimo del webhook
/// (<c>POST /avisos/{token}</c>), con ventana fija de un minuto por IP (plan §1.5: más de 60 por minuto ⇒ 429) y, aparte, un límite
/// global de todas las IP juntas (<see cref="OpcionesLimitesSolicitudes.AvisosGlobalesPorMinuto"/>, más alto que el de cada IP). La
/// IP es la del cliente real detrás del proxy de confianza (<see cref="ProxiesConfiables"/>, que va antes en el pipeline). Un token
/// sin la forma (<see cref="Modulos.Avisos.AvisosEndpoints.TokenConForma"/>) no consume cupo: el endpoint responde 410 sin tocar
/// la base (IMP-3 / M-1). Por encima de un límite responde 429 <see cref="CodigoDemasiadasSolicitudes"/> con <c>Retry-After</c> y
/// deja un aviso en el log con la política (sin la IP completa ni el token). Los límites se leen de <see cref="Seccion"/>; un valor
/// menor que 1, o un global menor que el de cada IP, impide arrancar.
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
            limitador.AddPolicy(Avisos, ctx => TokenSinForma(ctx)
                ? RateLimitPartition.GetNoLimiter($"{Avisos}:sin-forma")
                : RateLimitPartition.GetFixedWindowLimiter(
                    $"{Avisos}:{ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida"}",
                    _ => PorMinuto(o.AvisosPorIpPorMinuto)));
            // El global corre antes que la política del endpoint: solo cuenta en el receptor de avisos y con token con forma.
            limitador.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                !EsAvisos(ctx) || TokenSinForma(ctx)
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetFixedWindowLimiter($"{Avisos}:global", _ => PorMinuto(o.AvisosGlobalesPorMinuto)));
            limitador.OnRejected = RechazarAsync;
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions PorMinuto(int permitidas) => new()
    {
        PermitLimit = permitidas,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static bool EsAvisos(HttpContext ctx) =>
        string.Equals(ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName, Avisos, StringComparison.Ordinal);

    private static bool TokenSinForma(HttpContext ctx) =>
        !Modulos.Avisos.AvisosEndpoints.TokenConForma(ctx.Request.RouteValues.TryGetValue("token", out var t) ? t as string : null);

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

    /// <summary>Avisos del webhook por minuto de todas las IP juntas (IMP-3); no puede ser menor que el de cada IP.</summary>
    public int AvisosGlobalesPorMinuto { get; set; } = 600;

    public void Validar()
    {
        if (AvisosPorIpPorMinuto < 1 || AvisosGlobalesPorMinuto < 1)
            throw new InvalidOperationException($"{LimitesSolicitudes.Seccion}: todos los límites deben ser al menos 1.");
        if (AvisosGlobalesPorMinuto < AvisosPorIpPorMinuto)
            throw new InvalidOperationException(
                $"{LimitesSolicitudes.Seccion}: AvisosGlobalesPorMinuto ({AvisosGlobalesPorMinuto}) no puede ser menor que AvisosPorIpPorMinuto ({AvisosPorIpPorMinuto}).");
    }
}
