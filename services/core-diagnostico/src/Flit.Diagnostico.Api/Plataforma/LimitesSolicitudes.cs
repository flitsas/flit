using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Límites de solicitudes de core-diagnostico (HU #14582, contrato-api §1.5; SP-5, B-6; DM-2). Tres políticas con nombre, expuestas como
/// constantes para que las rutas las pidan con <c>RequireRateLimiting</c>:
/// <list type="bullet">
/// <item><see cref="Express"/>: <c>POST /express</c> y <c>POST /express/estimado</c>, ventana fija de un minuto por usuario.</item>
/// <item><see cref="Confirmacion"/>: <c>POST /diagnosticos/{id}/vehiculos</c>, un minuto por usuario y, además, por compañía.</item>
/// <item><see cref="Invitaciones"/>: solo se registra aquí (la aplica be2b en invitar), una hora por actor y por compañía.</item>
/// </list>
/// La partición del usuario es el <c>sub</c> del token y la de la compañía, el <c>tenant_id</c>: nunca un documento ni la placa. El
/// cupo por compañía va en el limitador global, que solo limita las rutas cuya política lo tiene; el del usuario, en la política. Por
/// encima del límite: 429 <see cref="CodigoDemasiadasSolicitudes"/> con <c>Retry-After</c> y un aviso en el log con la política, la
/// compañía y el usuario (ids), para revisar un sondeo del oráculo de <c>cambio_propietario</c> (SP-5). Los límites se leen de
/// <see cref="Seccion"/> (<see cref="OpcionesLimitesSolicitudes"/>).
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddLimitesSolicitudes(builder.Configuration);
/// app.UseRateLimiter(); // después de UseAuthentication / UseAuthorization
/// api.MapPost("/express", Crear).RequireRateLimiting(LimitesSolicitudes.Express);
/// </code>
/// </example>
internal static partial class LimitesSolicitudes
{
    public const string Express = "diagnostico-express";
    public const string Confirmacion = "diagnostico-confirmacion";
    public const string Invitaciones = "diagnostico-invitaciones";
    public const string CodigoDemasiadasSolicitudes = "DEMASIADAS_SOLICITUDES";
    public const string Seccion = "Diagnostico:LimitesSolicitudes";

    private const string SinIdentidad = "anonimo";

    public static IServiceCollection AddLimitesSolicitudes(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = configuration.GetSection(Seccion).Get<OpcionesLimitesSolicitudes>() ?? new OpcionesLimitesSolicitudes();
        o.Validar();
        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limitador.AddPolicy(Express, ctx => PorMinuto(Express, Usuario(ctx), o.ExpressPorUsuarioPorMinuto));
            limitador.AddPolicy(Confirmacion, ctx => PorMinuto(Confirmacion, Usuario(ctx), o.ConfirmacionPorUsuarioPorMinuto));
            limitador.AddPolicy(Invitaciones, ctx => PorHora(Invitaciones, Usuario(ctx), o.InvitacionesPorActorPorHora));
            limitador.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                PoliticaDe(ctx) switch
                {
                    Confirmacion => PorMinuto("compania-" + Confirmacion, Compania(ctx), o.ConfirmacionPorCompaniaPorMinuto),
                    Invitaciones => PorHora("compania-" + Invitaciones, Compania(ctx), o.InvitacionesPorCompaniaPorHora),
                    _ => RateLimitPartition.GetNoLimiter(string.Empty),
                });
            limitador.OnRejected = RechazarAsync;
        });
        return services;
    }

    private static string? PoliticaDe(HttpContext ctx) => ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static string Usuario(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value is { Length: > 0 } sub ? sub : SinIdentidad;

    private static string Compania(HttpContext ctx) => ctx.User.FindFirst("tenant_id")?.Value is { Length: > 0 } tenant ? tenant : SinIdentidad;

    private static RateLimitPartition<string> PorMinuto(string prefijo, string clave, int permisos) => Ventana(prefijo, clave, permisos, TimeSpan.FromMinutes(1));

    private static RateLimitPartition<string> PorHora(string prefijo, string clave, int permisos) => Ventana(prefijo, clave, permisos, TimeSpan.FromHours(1));

    private static RateLimitPartition<string> Ventana(string prefijo, string clave, int permisos, TimeSpan ventana) =>
        RateLimitPartition.GetFixedWindowLimiter($"{prefijo}:{clave}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permisos,
            Window = ventana,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static async ValueTask RechazarAsync(OnRejectedContext contexto, CancellationToken ct)
    {
        var http = contexto.HttpContext;
        var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera) ? Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds)) : 60;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LimitesSolicitudes).FullName!);
        Rechazada(log, PoliticaDe(http) ?? "global", Compania(http), Usuario(http), segundos);
        await http.Response.WriteAsJsonAsync(
            new { code = CodigoDemasiadasSolicitudes, error = $"Demasiadas solicitudes seguidas. Espera {segundos} segundos y vuelve a intentarlo." }, ct)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 14582, Level = LogLevel.Warning,
        Message = "Límite de solicitudes {Politica} superado (compañía {Compania}, usuario {Usuario}); reintento en {Segundos} s")]
    private static partial void Rechazada(ILogger logger, string politica, string compania, string usuario, int segundos);
}

/// <summary>
/// Límites de <see cref="LimitesSolicitudes"/> (sección <c>Diagnostico:LimitesSolicitudes</c>). Por defecto los del contrato: Express
/// 30/min por usuario; confirmación 10/min por usuario y 60/min por compañía; invitaciones 20/h por actor y 100/h por compañía. Un
/// valor menor que 1 impide arrancar.
/// </summary>
internal sealed class OpcionesLimitesSolicitudes
{
    public int ExpressPorUsuarioPorMinuto { get; set; } = 30;

    public int ConfirmacionPorUsuarioPorMinuto { get; set; } = 10;

    public int ConfirmacionPorCompaniaPorMinuto { get; set; } = 60;

    public int InvitacionesPorActorPorHora { get; set; } = 20;

    public int InvitacionesPorCompaniaPorHora { get; set; } = 100;

    public void Validar()
    {
        int[] valores =
        [
            ExpressPorUsuarioPorMinuto, ConfirmacionPorUsuarioPorMinuto, ConfirmacionPorCompaniaPorMinuto, InvitacionesPorActorPorHora,
            InvitacionesPorCompaniaPorHora,
        ];
        if (valores.Any(v => v < 1))
            throw new InvalidOperationException($"{LimitesSolicitudes.Seccion}: todos los límites deben ser al menos 1.");
    }
}
