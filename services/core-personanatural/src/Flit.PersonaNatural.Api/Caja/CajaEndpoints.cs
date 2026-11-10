using System.Globalization;
using System.Security.Claims;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Portal de caja del organismo (Feature #14453, ADR-0082), bajo <c>/api/v1/persona-natural/caja</c>. Solo existe con
/// <c>Caja:Habilitado=true</c>. Autenticación: JWT con audiencia <c>caja</c> (<c>Platform:Auth:ExtraAudiences</c>), producto
/// <c>caja</c> y rol <c>ot_caja</c> (<see cref="PoliticaCaja"/>); además, en CADA petición, core-api dice quién es y de qué
/// organismo (<see cref="IResolverCajero"/>, sin caché). El organismo jamás viene del cliente. Errores: <c>{ code, message }</c>.
/// </summary>
public static class CajaEndpoints
{
    public const string PoliticaCaja = "CajaPolicy";
    public const string Base = SesionEndpoints.Base + "/caja";
    public const string CabeceraIdempotencia = "Idempotency-Key";
    public const string CodigoSinSesion = "sin_sesion";
    public const string CodigoSinPermiso = "sin_permiso";

    private const string ItemCajero = "flit.pn.cajero";

    private static readonly string[] AudienciasBase = [];

    public static IServiceCollection AddCajaPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = CajaOptions.Leer(configuration);
        services.TryAddSingleton(Options.Create(opciones));
        services.TryAddScoped<CajaAuditoria>();
        services.TryAddScoped<CajaTopeBusquedas>();
        services.TryAddScoped<CajaBusquedaService>();
        services.TryAddScoped<CajaConfirmacionService>();
        services.TryAddScoped<CajaListaService>();
        services.TryAddScoped<CajaAnulacionService>();
        services.TryAddScoped<RadicacionDePagadasService>();
        services.AddHostedService<RadicacionDePagadasJob>();
        services.TryAddScoped<IResolverCajero>(sp =>
            sp.GetService<PersonaNaturalService.PersonaNaturalServiceClient>() is { } cliente ? new ResolverCajeroGrpc(cliente) : new ResolverCajeroNoConfigurado());

        // El token del producto caja trae aud=caja: sin esto el BFF solo aceptaría aud=personanatural. Los endpoints de servicio gRPC
        // siguen exigiendo un token de servicio con scope, así que un token de caja jamás los pasa.
        if (opciones.Habilitado)
        {
            var extras = configuration.GetSection("Platform:Auth:ExtraAudiences").Get<string[]>() ?? AudienciasBase;
            string[] audiencias = [.. extras, CajaOptions.Audiencia];
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
            {
                var existentes = jwt.TokenValidationParameters.ValidAudiences ?? [];
                jwt.TokenValidationParameters.ValidAudiences = [.. existentes.Concat(audiencias).Distinct(StringComparer.Ordinal)];
            });
        }

        return services;
    }

    /// <summary>La política de caja: producto <c>caja</c>, rol <c>ot_caja</c> y audiencia <c>caja</c> en un token de usuario.</summary>
    public static void AgregarPoliticas(AuthorizationOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        o.AddPolicy(PoliticaCaja, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("product", CajaOptions.Audiencia)
            .RequireAssertion(ctx =>
                Guid.TryParse(ctx.User.FindFirstValue("sub"), out _)
                && ctx.User.FindAll("aud").Any(c => c.Value == CajaOptions.Audiencia)
                && ctx.User.Claims.Any(c => c.Type is "role" or "role_code" && string.Equals(c.Value, CajaOptions.Rol, StringComparison.Ordinal))));
    }

    public static IEndpointRouteBuilder MapCajaEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var opciones = app.ServiceProvider.GetRequiredService<IOptions<CajaOptions>>().Value;
        if (!opciones.Habilitado)
            return app;

        var grupo = app.MapGroup(Base).AllowAnonymous().RequireRateLimiting(Abuso.AbusoEndpoints.PoliticaPorIp);
        grupo.AddEndpointFilter(ConCajero);

        grupo.MapGet("/sesion", (HttpContext http) =>
        {
            var c = Cajero(http);
            var inicio = InicioDeSesion(http.User) ?? http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var expira = inicio.AddHours(http.RequestServices.GetRequiredService<IOptions<CajaOptions>>().Value.SesionTopeHoras);
            return Results.Json(new
            {
                usuario = new { id = c.UsuarioId, nombre = c.Nombre },
                organismo = new { id = c.OrganismoId, codigo = c.OrganismoCodigo, nombre = c.OrganismoNombre },
                expiraEn = expira,
            }, Punto.PuntoEndpoints.Json);
        });

        grupo.MapGet("/tramites/{codigo}", (string codigo, HttpContext http, CancellationToken ct) => Atender(http, async () =>
            Results.Json(await http.RequestServices.GetRequiredService<CajaBusquedaService>().BuscarAsync(Cajero(http), codigo, Ip(http), ct).ConfigureAwait(false), Punto.PuntoEndpoints.Json)));

        grupo.MapPost("/tramites/{codigo}/confirmar", (string codigo, HttpContext http, CancellationToken ct) => Atender(http, async () =>
        {
            var pedido = await LeerConfirmacionAsync(http, codigo, ct).ConfigureAwait(false);
            var r = await http.RequestServices.GetRequiredService<CajaConfirmacionService>().ConfirmarAsync(Cajero(http), pedido, Ip(http), ct).ConfigureAwait(false);
            return Results.Json(new { codigo = r.Codigo, valorCop = r.ValorCop, medio = r.Medio, medioDetalle = r.MedioDetalle, pagadoEn = r.PagadoEn, mensaje = "Pago registrado." }, Punto.PuntoEndpoints.Json);
        }));

        grupo.MapGet("/cobros/hoy", (int? pagina, int? tamano, HttpContext http, CancellationToken ct) => Atender(http, async () =>
            Results.Json(await http.RequestServices.GetRequiredService<CajaListaService>().HoyAsync(Cajero(http), pagina, tamano, ct).ConfigureAwait(false), Punto.PuntoEndpoints.Json)));

        return app;
    }

    internal static Cajero Cajero(HttpContext http) =>
        http.Items[ItemCajero] as Cajero ?? throw new InvalidOperationException("El cajero no está en la petición.");

    /// <summary>Inicio de la autenticación del usuario: <c>auth_time</c> si el token lo trae, si no <c>iat</c>.</summary>
    internal static DateTimeOffset? InicioDeSesion(ClaimsPrincipal user)
    {
        foreach (var tipo in new[] { "auth_time", "iat" })
        {
            if (long.TryParse(user.FindFirstValue(tipo), NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos))
                return DateTimeOffset.FromUnixTimeSeconds(segundos);
        }

        return null;
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Autentica (401 <c>sin_sesion</c>), autoriza por política (403 <c>sin_permiso</c>), impone el tope absoluto de sesión (401) y
    /// le pregunta a core-api quién es y de qué organismo (403 <c>sin_permiso</c> si ya no es un cajero activo).
    /// </summary>
    internal static async ValueTask<object?> ConCajero(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        if (http.User.Identity?.IsAuthenticated != true)
            return Error(StatusCodes.Status401Unauthorized, CodigoSinSesion, "Inicie sesión para continuar.");

        var autorizacion = http.RequestServices.GetRequiredService<IAuthorizationService>();
        if (!(await autorizacion.AuthorizeAsync(http.User, PoliticaCaja).ConfigureAwait(false)).Succeeded)
            return Error(StatusCodes.Status403Forbidden, CodigoSinPermiso, "Su usuario no puede cobrar en este organismo.");

        var reloj = http.RequestServices.GetRequiredService<TimeProvider>();
        var tope = TimeSpan.FromHours(http.RequestServices.GetRequiredService<IOptions<CajaOptions>>().Value.SesionTopeHoras);
        if (InicioDeSesion(http.User) is { } inicio && reloj.GetUtcNow() - inicio > tope)
            return Error(StatusCodes.Status401Unauthorized, CodigoSinSesion, "Inicie sesión para continuar.");

        if (!Guid.TryParse(http.User.FindFirstValue("sub"), out var usuario) || !Guid.TryParse(http.User.FindFirstValue("tenant_id"), out var empresa))
            return Error(StatusCodes.Status403Forbidden, CodigoSinPermiso, "Su usuario no puede cobrar en este organismo.");

        try
        {
            var cajero = await http.RequestServices.GetRequiredService<IResolverCajero>().ResolverAsync(empresa, usuario, http.RequestAborted).ConfigureAwait(false);
            if (cajero is null)
                return Error(StatusCodes.Status403Forbidden, CodigoSinPermiso, "Su usuario no puede cobrar en este organismo.");
            http.Items[ItemCajero] = cajero;
        }
        catch (FachadaException ex)
        {
            return Error(ex.EstadoHttp, ex.Codigo, ex.Message);
        }

        return await siguiente(contexto).ConfigureAwait(false);
    }

    private static async Task<PedidoDeConfirmacion> LeerConfirmacionAsync(HttpContext http, string codigo, CancellationToken ct)
    {
        var opciones = http.RequestServices.GetRequiredService<IOptions<CajaOptions>>().Value;
        if (!http.Request.HasFormContentType)
            throw new ReglaDeNegocioException(StatusCodes.Status400BadRequest, CajaConfirmacionService.CodigoMedioInvalido, "Solicitud no válida", "Indique cómo se cobró.");

        var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        long? visto = long.TryParse(form["totalVistoCop"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        string? nombre = null;
        byte[]? bytes = null;
        if (form.Files.GetFile("prefactura") is { } archivo)
        {
            nombre = archivo.FileName;
            if (archivo.Length > opciones.PrefacturaMaxBytes)
                throw new ReglaDeNegocioException(StatusCodes.Status400BadRequest, AdjuntosService.CodigoPrefacturaInvalida, "Prefactura no válida", $"La prefactura supera los {opciones.PrefacturaMaxBytes / (1024 * 1024)} MB permitidos.");
            await using var s = archivo.OpenReadStream();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        return new PedidoDeConfirmacion(codigo, form["medio"].ToString(), form["medioDetalle"].ToString(), visto, http.Request.Headers[CabeceraIdempotencia].ToString(), nombre, bytes);
    }

    /// <summary>Traduce las reglas del negocio a <c>{ code, message }</c> con el vocabulario de la caja.</summary>
    internal static async Task<IResult> Atender(HttpContext http, Func<Task<IResult>> accion)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            return await accion().ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex)
        {
            var (codigo, mensaje) = Traducir(ex);
            var cuerpo = new Dictionary<string, object?> { ["code"] = codigo, ["message"] = mensaje };
            if (ex.Extensiones is not null)
                foreach (var (k, valor) in ex.Extensiones.Where(e => e.Key != "retryAfterSegundos" && e.Key != "motivo"))
                    cuerpo[k] = valor;
            if (ex.Extensiones?.TryGetValue("retryAfterSegundos", out var espera) == true && espera is int segundos)
                http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
            return Results.Json(cuerpo, Punto.PuntoEndpoints.Json, statusCode: ex.Estado);
        }
        catch (FachadaException ex)
        {
            return Error(ex.EstadoHttp, ex.Codigo, ex.Message);
        }
    }

    /// <summary>Los códigos del pago compartido (<see cref="IRegistroDePagos"/>) a los de la tabla de errores de la caja.</summary>
    internal static (string Codigo, string Mensaje) Traducir(ReglaDeNegocioException ex) => ex.Codigo switch
    {
        "vencido" => (CajaBusquedaService.CodigoCobroVencido, "El pago en caja de este trámite venció."),
        "estado_no_permite_pago" or "no_habilitado" or "medio_no_disponible" or "total_cero" or "no_es_canal_punto" =>
            (CajaBusquedaService.CodigoNoHabilitado, "Este trámite no tiene el pago en caja habilitado."),
        "medio_caja_invalido" => (CajaConfirmacionService.CodigoMedioInvalido, ex.Message),
        "solicitud_no_encontrada" => (CajaBusquedaService.CodigoNoEncontrado, "No encontramos ese trámite."),
        CajaBusquedaService.CodigoYaPagado => (CajaBusquedaService.CodigoYaPagado, "Este trámite ya fue pagado."),
        CajaBusquedaService.CodigoOtroOrganismo => (CajaBusquedaService.CodigoOtroOrganismo, "Este trámite no es de este organismo."),
        _ => (ex.Codigo, ex.Message),
    };

    private static IResult Error(int http, string codigo, string mensaje) =>
        Results.Json(new { code = codigo, message = mensaje }, Punto.PuntoEndpoints.Json, statusCode: http);
}
