using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// <c>POST /api/v1/impuesto/avisos/{token}</c>: el webhook del proveedor de impuesto (HU #14711, plan §3.3). Anónimo (lo autentica
/// la firma), con los límites de <see cref="LimitesAvisosImpuesto"/> y el cuerpo crudo acotado a <see cref="MaxCuerpo"/> (413).
/// Ninguna respuesta lleva cuerpo: 200, 401, 413, 429 o 503. Un token sin forma responde 401 sin leer el cuerpo ni consumir cupo.
/// La lógica está en <see cref="ProcesadorAvisoImpuesto"/>. La ruta pública la expone el gateway (YARP/nginx).
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddLimitesAvisosImpuesto();
/// app.UseRateLimiter();
/// app.MapAvisosImpuesto();
/// </code>
/// </example>
internal static class AvisosImpuestoEndpoints
{
    public const string Prefijo = "/api/v1/impuesto/avisos";
    public const string Ruta = Prefijo + "/{token}";

    /// <summary>Tope del cuerpo crudo: el mismo de core-impuestos.</summary>
    public const int MaxCuerpo = 256 * 1024;

    public static IEndpointRouteBuilder MapAvisosImpuesto(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost(Ruta, RecibirAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(LimitesAvisosImpuesto.Politica)
            .WithName("RecibirAvisoProveedorImpuestoConsultas");
        return app;
    }

    internal static async Task<IResult> RecibirAsync(string token, HttpRequest request, ProcesadorAvisoImpuesto procesador, CancellationToken ct)
    {
        if (!TokenAvisoImpuesto.TieneForma(token))
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        if (request.ContentLength > MaxCuerpo)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var cuerpo = await LeerAcotadoAsync(request.Body, ct).ConfigureAwait(false);
        if (cuerpo is null)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var cabeceras = new CabecerasAvisoImpuesto(
            Cabecera(request, VerificadorFirmaImpuesto.CabeceraTimestamp),
            Cabecera(request, VerificadorFirmaImpuesto.CabeceraFirma),
            Cabecera(request, VerificadorFirmaImpuesto.CabeceraDelivery),
            Cabecera(request, VerificadorFirmaImpuesto.CabeceraEvento));
        return Results.StatusCode(await procesador.ProcesarAsync(token, cabeceras, cuerpo, ct).ConfigureAwait(false));
    }

    /// <summary>El cuerpo crudo, tal como llegó; null si pasa de <see cref="MaxCuerpo"/>.</summary>
    private static async Task<byte[]?> LeerAcotadoAsync(Stream cuerpo, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var trozo = new byte[16 * 1024];
        int leidos;
        while ((leidos = await cuerpo.ReadAsync(trozo, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + leidos > MaxCuerpo)
                return null;
            buffer.Write(trozo, 0, leidos);
        }

        return buffer.ToArray();
    }

    private static string? Cabecera(HttpRequest request, string nombre) =>
        request.Headers.TryGetValue(nombre, out var v) && v.Count > 0 ? v[0] : null;
}

/// <summary>
/// Límites del receptor de avisos (HU #14711), con los valores de <c>LimitesSolicitudes</c> de core-impuestos: ventana fija de un minuto
/// con <see cref="PorIpPorMinuto"/> por IP y <see cref="GlobalesPorMinuto"/> de todas las IP juntas, solo en este endpoint. Un token sin
/// forma no consume cupo (el endpoint responde 401 sin trabajo). El global solo lo consume un token válido (forma y HMAC, L1); uno con
/// forma y HMAC inválido gasta solo el cupo de su IP. Por encima: 429 con <c>Retry-After</c>, sin cuerpo, y un aviso en el
/// log solo con la política. La IP es la de la conexión: detrás del gateway hereda el pendiente G-6 de core-impuestos.
/// </summary>
internal static partial class LimitesAvisosImpuesto
{
    public const string Politica = "consultas-impuesto-avisos";
    public const int PorIpPorMinuto = 60;
    public const int GlobalesPorMinuto = 600;

    public static IServiceCollection AddLimitesAvisosImpuesto(this IServiceCollection services)
    {
        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limitador.AddPolicy(Politica, ctx => TokenSinForma(ctx)
                ? RateLimitPartition.GetNoLimiter($"{Politica}:sin-forma")
                : RateLimitPartition.GetFixedWindowLimiter(
                    $"{Politica}:{ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida"}",
                    _ => PorMinuto(PorIpPorMinuto)));
            // El global corre antes que la política del endpoint: solo cuenta en el receptor de avisos y con un token VÁLIDO (L1): el
            // HMAC del token se verifica aquí con la llave del servicio. Un token con forma pero HMAC inválido no lo toca y solo gasta
            // el cupo de su IP, así que nadie sin la llave agota el cupo de todas las empresas fabricando tokens desde muchas IP.
            limitador.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                !EsAvisos(ctx) || !TokenValido(ctx)
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetFixedWindowLimiter($"{Politica}:global", _ => PorMinuto(GlobalesPorMinuto)));
            limitador.OnRejected = (contexto, _) =>
            {
                var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
                    ? Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds))
                    : 60;
                contexto.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
                Rechazada(contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LimitesAvisosImpuesto).FullName!), segundos);
                return ValueTask.CompletedTask;
            };
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
        string.Equals(ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName, Politica, StringComparison.Ordinal);

    private static string? Token(HttpContext ctx) => ctx.Request.RouteValues.TryGetValue("token", out var t) ? t as string : null;

    private static bool TokenSinForma(HttpContext ctx) => !TokenAvisoImpuesto.TieneForma(Token(ctx));

    /// <summary>Forma y HMAC del token con la llave del servicio; sin llave ningún token es válido (el receptor responde 401).</summary>
    private static bool TokenValido(HttpContext ctx) =>
        ctx.RequestServices.GetService<OpcionesAvisoImpuesto>()?.Llave is { } llave && TokenAvisoImpuesto.TryLeer(Token(ctx), llave, out _);

    [LoggerMessage(EventId = 7460, Level = LogLevel.Warning, Message = "Límite de avisos de impuesto superado; reintento en {Segundos} s.")]
    private static partial void Rechazada(ILogger logger, int segundos);
}
