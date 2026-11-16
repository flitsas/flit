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
/// La partición del usuario es el <c>sub</c> del token y la de la compañía, el <c>tenant_id</c>: nunca un documento ni la placa. En
/// confirmación e invitaciones los dos cupos van encadenados en la misma política, usuario → compañía (<see cref="LimitadorEncadenado"/>,
/// CR-1 del review): el de la compañía solo se gasta si el del usuario concede, así que un usuario que insiste no agota el cupo de
/// toda su compañía. Por encima del límite: 429 <see cref="CodigoDemasiadasSolicitudes"/> con <c>Retry-After</c> y un aviso en el log con la política, la
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
            var companiasConfirmacion = CupoPorCompania(o.ConfirmacionPorCompaniaPorMinuto, TimeSpan.FromMinutes(1));
            var companiasInvitaciones = CupoPorCompania(o.InvitacionesPorCompaniaPorHora, TimeSpan.FromHours(1));
            limitador.AddPolicy(Confirmacion, ctx =>
                Encadenado(Confirmacion, Usuario(ctx), Compania(ctx), o.ConfirmacionPorUsuarioPorMinuto, TimeSpan.FromMinutes(1), companiasConfirmacion));
            limitador.AddPolicy(Invitaciones, ctx =>
                Encadenado(Invitaciones, Usuario(ctx), Compania(ctx), o.InvitacionesPorActorPorHora, TimeSpan.FromHours(1), companiasInvitaciones));
            limitador.OnRejected = RechazarAsync;
        });
        return services;
    }

    private static string? PoliticaDe(HttpContext ctx) => ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static string Usuario(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value is { Length: > 0 } sub ? sub : SinIdentidad;

    private static string Compania(HttpContext ctx) => ctx.User.FindFirst("tenant_id")?.Value is { Length: > 0 } tenant ? tenant : SinIdentidad;

    private static RateLimitPartition<string> PorMinuto(string prefijo, string clave, int permisos) => Ventana(prefijo, clave, permisos, TimeSpan.FromMinutes(1));

    private static RateLimitPartition<string> Ventana(string prefijo, string clave, int permisos, TimeSpan ventana) =>
        RateLimitPartition.GetFixedWindowLimiter($"{prefijo}:{clave}", _ => OpcionesVentana(permisos, ventana));

    /// <summary>Un cupo de ventana fija por compañía (clave <c>tenant_id</c>), compartido por todos los usuarios de la política.</summary>
    private static PartitionedRateLimiter<string> CupoPorCompania(int permisos, TimeSpan ventana) =>
        PartitionedRateLimiter.Create<string, string>(compania => RateLimitPartition.GetFixedWindowLimiter(compania, _ => OpcionesVentana(permisos, ventana)));

    /// <summary>Partición por usuario y compañía: el cupo del usuario y, solo si concede, el de su compañía.</summary>
    private static RateLimitPartition<string> Encadenado(
        string politica, string usuario, string compania, int permisosUsuario, TimeSpan ventana, PartitionedRateLimiter<string> companias) =>
        RateLimitPartition.Get($"{politica}:{usuario}|{compania}", _ =>
            new LimitadorEncadenado(new FixedWindowRateLimiter(OpcionesVentana(permisosUsuario, ventana)), companias, compania));

    private static FixedWindowRateLimiterOptions OpcionesVentana(int permisos, TimeSpan ventana) => new()
    {
        PermitLimit = permisos,
        Window = ventana,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

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
/// Limitador de dos niveles (CR-1 del review de be1-b): pide primero el cupo del usuario (<paramref name="usuario"/>, propio de la
/// partición) y, solo si lo concede, el de la compañía en <paramref name="companias"/> (compartido). Si el de la compañía rechaza, se
/// descarta la concesión del usuario (en ventana fija ese permiso ya cuenta para él, no para la compañía) y se devuelve el rechazo de
/// la compañía (con su <c>Retry-After</c>). Sin cola: todo es inmediato. Al
/// liberarse la partición solo se descarta el limitador del usuario; el de la compañía sigue vivo para los demás.
/// </summary>
internal sealed class LimitadorEncadenado(RateLimiter usuario, PartitionedRateLimiter<string> companias, string compania) : RateLimiter
{
    public override TimeSpan? IdleDuration => usuario.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => usuario.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var deUsuario = usuario.AttemptAcquire(permitCount);
        if (!deUsuario.IsAcquired)
            return deUsuario;
        var deCompania = companias.AttemptAcquire(compania, permitCount);
        if (deCompania.IsAcquired)
            return new LeaseDoble(deUsuario, deCompania);
        deUsuario.Dispose();
        return deCompania;
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(permitCount));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            usuario.Dispose();
        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await usuario.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private sealed class LeaseDoble(RateLimitLease primero, RateLimitLease segundo) : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                primero.Dispose();
                segundo.Dispose();
            }

            base.Dispose(disposing);
        }
    }
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
