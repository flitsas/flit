using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Abuso;

/// <summary>
/// Piezas HTTP de la protección anti-abuso que no son endpoints (HU #14036): IP del ciudadano detrás de los proxies, errores
/// sin detalle y tope global del cuerpo. Se arma con <see cref="AddAntiAbusoHttp"/> y <see cref="UseAntiAbusoHttp"/>.
/// </summary>
public static class AntiAbusoHttp
{
    public const string CodigoErrorInterno = "error_interno";
    public const string CodigoCuerpoGrande = "cuerpo_demasiado_grande";
    public const string CodigoSolicitudInvalida = "solicitud_invalida";

    public static IServiceCollection AddAntiAbusoHttp(this IServiceCollection services, IConfiguration configuration)
    {
        // Las opciones se leen del IConfiguration para respetar lo que los tests y el ambiente sobreescriban antes de Build.
        services.Configure<ForwardedHeadersOptions>(o => ForwardedHeadersConfianza.Aplicar(o, Leer(configuration), ForwardedHeadersConfianza.LeerRedes(configuration)));
        return services;
    }

    internal static AbusoOptions Leer(IConfiguration configuration) =>
        configuration.GetSection(AbusoOptions.SectionName).Get<AbusoOptions>() ?? new AbusoOptions();

    /// <summary>
    /// Orden: manejador de errores (envuelve todo), IP real (si se confía en <c>X-Forwarded-For</c>) y tope de cuerpo, todo
    /// antes del enrutamiento y del binding.
    /// </summary>
    public static WebApplication UseAntiAbusoHttp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseExceptionHandler(rama => rama.Run(EscribirErrorAsync));
        if (Leer(app.Configuration).ConfiarEnXForwardedFor)
            app.UseForwardedHeaders();
        app.Use(LimiteDeCuerpoAsync);
        return app;
    }

    /// <summary>
    /// Respuesta de una excepción no manejada: <c>application/problem+json</c> con código estable. Nunca lleva traza, mensaje
    /// de la excepción ni rutas, tampoco en Development (donde la página de desarrollador mostraría todo eso).
    /// </summary>
    private static async Task EscribirErrorAsync(HttpContext http)
    {
        var excepcion = http.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (estado, titulo, codigo) = excepcion is BadHttpRequestException malo
            ? malo.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? (StatusCodes.Status413PayloadTooLarge, "Cuerpo demasiado grande", CodigoCuerpoGrande)
                : (StatusCodes.Status400BadRequest, "Solicitud inválida", CodigoSolicitudInvalida)
            : (StatusCodes.Status500InternalServerError, "Error interno", CodigoErrorInterno);

        var problema = Results.Problem(
            statusCode: estado, title: titulo,
            extensions: new Dictionary<string, object?> { ["code"] = codigo, ["traceId"] = http.TraceIdentifier });
        await problema.ExecuteAsync(http).ConfigureAwait(false);
    }

    /// <summary>
    /// Tope global del cuerpo JSON. Las cargas de archivos (multipart, <c>…/adjuntos</c> y <c>…/pago/comprobante</c>) quedan
    /// fuera: tienen su propio tope por tipo y por configuración. Con <c>Content-Length</c> se rechaza sin leer nada; sin él
    /// (chunked) el flujo se envuelve y corta al pasar el tope.
    /// </summary>
    private static async Task LimiteDeCuerpoAsync(HttpContext http, RequestDelegate siguiente)
    {
        var tope = http.RequestServices.GetRequiredService<IOptions<AbusoOptions>>().Value.MaxCuerpoJsonBytes;
        if (tope <= 0 || EsCargaDeArchivo(http.Request))
        {
            await siguiente(http).ConfigureAwait(false);
            return;
        }

        if (http.Request.ContentLength is { } largo && largo > tope)
        {
            await Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge, title: "Cuerpo demasiado grande",
                extensions: new Dictionary<string, object?> { ["code"] = CodigoCuerpoGrande }).ExecuteAsync(http).ConfigureAwait(false);
            return;
        }

        var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = tope;
        if (http.Request.ContentLength is null && http.Request.Body != Stream.Null)
            http.Request.Body = new FlujoLimitado(http.Request.Body, tope);
        await siguiente(http).ConfigureAwait(false);
    }

    private static bool EsCargaDeArchivo(HttpRequest peticion) =>
        peticion.HasFormContentType
        || peticion.Path.Value?.EndsWith("/adjuntos", StringComparison.OrdinalIgnoreCase) == true
        || peticion.Path.Value?.EndsWith("/pago/comprobante", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Redes de confianza y saltos para <c>X-Forwarded-For</c> (HU #14036).</summary>
public static class ForwardedHeadersConfianza
{
    /// <summary>Loopback y las redes privadas que usan Docker y las VPS (RFC1918, ULA de IPv6).</summary>
    public static readonly IReadOnlyList<string> RedesPorDefecto =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    /// <summary>
    /// Lee <c>Abuso:RedesDeConfianza</c> (lista indexada o CSV en una sola variable). Sin la clave: las privadas por defecto;
    /// con la clave pero vacía: ninguna red es de confianza. Se lee a mano porque el binder omite los valores vacíos.
    /// </summary>
    internal static string[]? LeerRedes(IConfiguration configuration)
    {
        var seccion = configuration.GetSection($"{AbusoOptions.SectionName}:RedesDeConfianza");
        var hijos = seccion.GetChildren().ToList();
        if (hijos.Count == 0 && seccion.Value is null)
            return null;
        var valores = hijos.Count > 0 ? hijos.Select(h => h.Value) : [seccion.Value];
        return [.. valores.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];
    }

    internal static void Aplicar(ForwardedHeadersOptions o, AbusoOptions abuso, string[]? redes)
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        o.ForwardLimit = Math.Max(1, abuso.SaltosDeConfianza);
        // Solo se confía en lo configurado: el recorrido se detiene en el primer salto que no sea de una red de confianza,
        // de modo que un X-Forwarded-For falseado por el cliente nunca reemplaza a la IP que vio el proxy de confianza.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var red in redes ?? [.. RedesPorDefecto])
        {
            if (string.IsNullOrWhiteSpace(red))
                continue;
            if (!System.Net.IPNetwork.TryParse(red.Trim(), out var parseada))
                throw new InvalidOperationException($"Abuso__RedesDeConfianza: «{red}» no es una red válida (use CIDR, p. ej. 10.0.0.0/8).");
            o.KnownIPNetworks.Add(parseada);
        }

        // ASP.NET trata las dos listas vacías como «confiar en todos» (justo el hueco que había con Clear()). Para «ninguna red
        // de confianza» se deja una sola dirección que nunca es de un cliente (broadcast), así la verificación sigue activa.
        if (o.KnownIPNetworks.Count == 0)
            o.KnownProxies.Add(System.Net.IPAddress.Broadcast);
    }
}

/// <summary>Envoltorio de lectura que corta con 413 cuando el cuerpo (sin <c>Content-Length</c>) pasa el tope.</summary>
internal sealed class FlujoLimitado(Stream interno, long tope) : Stream
{
    private long _leidos;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => Contar(interno.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Contar(await interno.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Contar(int leidos)
    {
        _leidos += leidos;
        if (_leidos > tope)
            throw new BadHttpRequestException("Cuerpo demasiado grande.", StatusCodes.Status413PayloadTooLarge);
        return leidos;
    }
}
