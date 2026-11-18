using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.PersonaNatural;
using Flit.Queries.Domain.Time;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// La caja del organismo como módulo de FLIT 2.0 (Feature #14453, Épica #14210, ADR-0082), bajo <c>/api/v1/ot/caja</c>. La usan el usuario
/// <c>ot_caja</c> (que inicia sesión en FLIT 2.0 pero solo puede usar la caja) y el administrador del OT (cobra, ve y anula). El
/// SuperAdmin NO entra por aquí: anula desde la administración del organismo (<c>/api/v1/admin/ot/portal-caja/cobros</c>).
/// <para>
/// core-api es la fachada y el cobro vive en el portal de Persona Natural (<c>CajaAdminService</c> por gRPC con <c>svc-tramites</c>). El
/// organismo y el actor salen SIEMPRE del token (y de la base: el usuario <c>ot_caja</c> se revalida en cada petición, sin caché), nunca
/// del cliente. Las respuestas JSON son las mismas que daba el portal de caja anterior (<c>{ code, message }</c> en los errores, con los
/// mismos códigos), para que el front reutilice sus tipos. Las rutas solo existen con <c>PortalCaja:Habilitado=true</c>; el portal caído
/// responde 503 <c>portal_no_disponible</c>.
/// </para>
/// </summary>
public static class OtCajaEndpoints
{
    public const string BasePath = "/api/v1/ot/caja";
    public const string CabeceraIdempotencia = "Idempotency-Key";

    public const string CodigoSinPermiso = "sin_permiso";
    public const string CodigoPortalNoDisponible = "portal_no_disponible";
    public const string CodigoMedioInvalido = "medio_invalido";
    public const string CodigoPrefacturaInvalida = "prefactura_invalida";
    public const string CodigoFechaInvalida = "fecha_invalida";
    public const string CodigoMotivoRequerido = "motivo_requerido";
    public const string CodigoMotivoLongitud = "motivo_longitud";
    public const string CodigoVersionRequerida = "version_requerida";
    public const string CodigoCobroNoAnulable = "cobro_no_anulable";
    public const string CodigoCobroNoEncontrado = "cobro_no_encontrado";

    public const int MotivoMinimo = 10;
    public const int MotivoMaximo = 300;
    public const int PrefacturaMaxBytes = 5 * 1024 * 1024;
    public const int TamanoPorDefecto = 10;
    public const int TamanoMaximo = 50;

    private const long SobrecargaMultipart = 1024 * 1024;
    private const string ItemActor = "flit.ot.caja.actor";

    public sealed record AnularCajaRequest(string? Motivo, string? Reason, long? RowVersion);

    public static IEndpointRouteBuilder MapOtCajaEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(BasePath)
            .RequireAuthorization()
            .AddEndpointFilter(PortalCajaHabilitadoFilter.Instance)
            .AddEndpointFilter(ConActorAsync)
            .WithTags("OT · Caja");

        group.MapGet("/tramites/{codigo}", BuscarAsync)
            .WithName("OtCajaBuscarTramite")
            .WithSummary("Busca el trámite por su ID (PN-AAAA-NNNNNN) y devuelve el desglose a cobrar; cuenta contra el tope de búsquedas sin resultado")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests).Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/tramites/{codigo}/confirmar", ConfirmarAsync)
            .WithName("OtCajaConfirmarCobro")
            .WithSummary("Confirma el cobro en caja: medio obligatorio, prefactura opcional (PDF/JPG/PNG de hasta 5 MB) e Idempotency-Key obligatoria")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/cobros", ListarAsync)
            .WithName("OtCajaCobros")
            .WithSummary("Cobros en caja del organismo en un día de Colombia (por defecto hoy), con el total del día sin los anulados")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/cobros/{cobroId:guid}/anular", AnularAsync)
            .WithName("OtCajaAnularCobro")
            .WithSummary("Anula un cobro en caja con motivo obligatorio. Solo el administrador del OT; nunca el usuario ot_caja")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    // ── Autorización ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Solo el rol <c>ot_caja</c> o el <c>ot_admin</c> de un organismo (el SuperAdmin no). El usuario <c>ot_caja</c> se revalida contra la
    /// base en cada petición (activo, sin suspensión, rol vigente, producto caja encendido, organismo activo); el administrador usa el
    /// organismo de su tenant. Sin pasar esto no se ejecuta nada.
    /// </summary>
    private static async ValueTask<object?> ConActorAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var user = http.User;
        var roles = user.Claims.Where(c => c.Type == AdminAuthorization.RoleClaimType).Select(c => c.Value).ToList();
        bool Tiene(string rol) => roles.Exists(r => string.Equals(r, rol, StringComparison.OrdinalIgnoreCase));

        var esAdmin = Tiene(AdminAuthorization.OtAdminRole);
        var esCaja = Tiene(AdminAuthorization.OtCajaRole);
        if (Tiene(AdminAuthorization.SuperAdminRole) || (!esAdmin && !esCaja)
            || AdminCompaniesDomainEndpoints.ResolveUserId(user) is not { } usuarioId
            || !RequestTenantResolver.TryResolveTenantId(user, out var tenantId))
            return SinPermiso();

        var actor = await http.RequestServices.GetRequiredService<IActorCajaStore>().ResolverAsync(esAdmin, usuarioId, tenantId, http.RequestAborted).ConfigureAwait(false);
        if (actor is null)
            return SinPermiso();

        http.Items[ItemActor] = actor;
        return await siguiente(contexto).ConfigureAwait(false);
    }

    private static ActorCaja Actor(HttpContext http) =>
        http.Items[ItemActor] as ActorCaja ?? throw new InvalidOperationException("El actor de caja no está en la petición.");

    private static IResult SinPermiso() =>
        Error(StatusCodes.Status403Forbidden, CodigoSinPermiso, "Su usuario no puede cobrar en este organismo.");

    // ── Endpoints ───────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> BuscarAsync(string codigo, HttpContext http, CancellationToken ct)
    {
        var actor = Actor(http);
        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>(); // solo existe con PortalCaja:Habilitado
        try
        {
            var t = await portal.BuscarAsync(actor.OrganismoId, codigo ?? string.Empty, actor.UsuarioId, HuellaDeIp(http), ct).ConfigureAwait(false);
            return Results.Json(new
            {
                codigo = t.Codigo,
                tipoTramite = new { codigo = t.TipoCodigo, nombre = t.TipoNombre },
                organismo = new { codigo = actor.OrganismoCodigo, nombre = actor.OrganismoNombre },
                lineas = t.Lineas.Select(l => new { concepto = l.Concepto, valorCop = l.ValorCop }),
                totalCop = t.TotalCop,
                versionTarifa = t.VersionTarifa,
                venceEn = t.VenceEn,
            }, JsonOptions);
        }
        catch (CajaPortalRechazoException ex)
        {
            return Rechazo(http, ex);
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    private static async Task<IResult> ConfirmarAsync(string codigo, HttpContext http, CancellationToken ct)
    {
        var actor = Actor(http);
        if (!http.Request.HasFormContentType)
            return Error(StatusCodes.Status400BadRequest, CodigoMedioInvalido, "Indique cómo se cobró.");

        // El tope del cuerpo se fija ANTES de leerlo: un archivo enorme no llega a la memoria del servicio.
        var tope = PrefacturaMaxBytes + SobrecargaMultipart;
        var limite = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is { IsReadOnly: false })
            limite.MaxRequestBodySize = tope;
        if (http.Request.ContentLength > tope)
            return Error(StatusCodes.Status413PayloadTooLarge, CodigoPrefacturaInvalida, MensajePrefacturaGrande);

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException or IOException)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, CodigoPrefacturaInvalida, MensajePrefacturaGrande);
        }

        // Nombres del formulario: medioCaja / detalle / totalVisto (los del contrato nuevo) y, por compatibilidad con el portal anterior,
        // medio / medioDetalle / totalVistoCop.
        var medio = Campo(form, "medioCaja", "medio");
        var detalle = Campo(form, "detalle", "medioDetalle");
        var textoTotal = Campo(form, "totalVisto", "totalVistoCop");
        long? visto = long.TryParse(textoTotal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        byte[]? bytes = null;
        string? nombre = null;
        string? tipo = null;
        if (form.Files.GetFile("prefactura") is { } archivo)
        {
            if (archivo.Length == 0)
                return Error(StatusCodes.Status400BadRequest, CodigoPrefacturaInvalida, "La prefactura está vacía.");
            if (archivo.Length > PrefacturaMaxBytes)
                return Error(StatusCodes.Status400BadRequest, CodigoPrefacturaInvalida, MensajePrefacturaGrande);
            nombre = archivo.FileName;
            tipo = archivo.ContentType;
            await using var s = archivo.OpenReadStream();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>();
        try
        {
            var r = await portal.ConfirmarAsync(
                new CajaConfirmacion(
                    actor.OrganismoId, codigo ?? string.Empty, actor.UsuarioId, actor.Nombre, medio, detalle, visto,
                    http.Request.Headers[CabeceraIdempotencia].ToString(), bytes, tipo, nombre, HuellaDeIp(http)),
                ct).ConfigureAwait(false);
            return Results.Json(
                new { codigo = r.Codigo, valorCop = r.ValorCop, medio = r.Medio, medioDetalle = r.MedioDetalle, pagadoEn = r.PagadoEn, mensaje = "Pago registrado." }, JsonOptions);
        }
        catch (CajaPortalRechazoException ex)
        {
            return Rechazo(http, ex);
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http, CancellationToken ct, [FromQuery] string? date = null, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var actor = Actor(http);
        var reloj = http.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        DateOnly fecha;
        if (string.IsNullOrWhiteSpace(date))
            fecha = ColombiaTime.Today(reloj);
        else if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            return Error(StatusCodes.Status400BadRequest, CodigoFechaInvalida, "La fecha debe ser AAAA-MM-DD.");

        var pagina = Math.Max(1, page ?? 1);
        var tamano = Math.Clamp(pageSize is null or <= 0 ? TamanoPorDefecto : pageSize.Value, 1, TamanoMaximo);
        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>();
        try
        {
            var texto = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var r = await portal.ListarAsync(actor.OrganismoId, texto, pagina, tamano, ct).ConfigureAwait(false);
            return Results.Json(new
            {
                fecha = texto,
                totalDiaCop = r.TotalDiaCop,
                items = r.Items.Select(c =>
                {
                    var pagadoEn = DateTimeOffset.Parse(c.PagadoEn, CultureInfo.InvariantCulture);
                    return new
                    {
                        codigo = c.Codigo,
                        valorCop = c.ValorCop,
                        medio = c.Medio,
                        medioDetalle = c.MedioDetalle.Length == 0 ? null : c.MedioDetalle,
                        hora = ColombiaTime.From(pagadoEn).ToString("HH:mm", CultureInfo.InvariantCulture),
                        pagadoEn,
                        usuario = c.PagadoPorNombre,
                        estado = c.Estado,
                        // Para anular (solo el administrador del OT lo usa).
                        cobroId = c.CobroId,
                        anulable = c.Anulable,
                        motivoNoAnulable = c.MotivoNoAnulable.Length == 0 ? null : c.MotivoNoAnulable,
                        anuladoEn = c.AnuladoEn.Length == 0 ? null : c.AnuladoEn,
                        anuladoPorNombre = c.AnuladoPorNombre.Length == 0 ? null : c.AnuladoPorNombre,
                        anulacionMotivo = c.AnulacionMotivo.Length == 0 ? null : c.AnulacionMotivo,
                        rowVersion = c.RowVersion,
                    };
                }),
                total = r.Total,
                pagina,
                tamano,
            }, JsonOptions);
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    private static async Task<IResult> AnularAsync(Guid cobroId, AnularCajaRequest? body, HttpContext http, CancellationToken ct)
    {
        var actor = Actor(http);
        if (!actor.EsAdmin)
            return Error(StatusCodes.Status403Forbidden, CodigoSinPermiso, "Solo el administrador del organismo puede anular un cobro.");

        var motivo = (body?.Motivo ?? body?.Reason)?.Trim();
        if (string.IsNullOrEmpty(motivo))
            return Error(StatusCodes.Status400BadRequest, CodigoMotivoRequerido, "Escriba el motivo de la anulación.");
        if (motivo.Length is < MotivoMinimo or > MotivoMaximo)
            return Error(StatusCodes.Status400BadRequest, CodigoMotivoLongitud, $"El motivo debe tener entre {MotivoMinimo} y {MotivoMaximo} caracteres.");

        // Sin la versión del cobro no hay control de concurrencia: se exige (viene de la lista) en vez de tratar 0 como «cualquiera».
        if (body?.RowVersion is not > 0)
            return Error(StatusCodes.Status400BadRequest, CodigoVersionRequerida, "Falta la versión del cobro. Actualice la lista e intente de nuevo.");

        var portal = http.RequestServices.GetRequiredService<ICajaAdminClient>();
        try
        {
            var r = await portal.AnularAsync(actor.OrganismoId, cobroId, motivo, actor.UsuarioId, actor.Nombre, body.RowVersion.Value, ct).ConfigureAwait(false);
            return r.Razon switch
            {
                null => Results.Json(new { cobroId, estado = r.Estado }, JsonOptions),
                CajaAnulacionResultado.NoAnulable => Error(
                    StatusCodes.Status409Conflict, CodigoCobroNoAnulable, "Este cobro ya no se puede anular: el trámite llegó a FLIT 2.0 o pasó la ventana de anulación."),
                _ => Error(StatusCodes.Status404NotFound, CodigoCobroNoEncontrado, "No encontramos ese cobro en este organismo."),
            };
        }
        catch (CajaPortalNoDisponibleException)
        {
            return PortalNoDisponible();
        }
    }

    // ── Soporte ─────────────────────────────────────────────────────────────────────────────────────────────────

    private const string MensajePrefacturaGrande = "La prefactura supera los 5 MB permitidos.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string Campo(IFormCollection form, string principal, string alterno)
    {
        var valor = form[principal].ToString();
        return valor.Length > 0 ? valor : form[alterno].ToString();
    }

    /// <summary>SHA-256 hexadecimal de la IP del cliente: la IP en claro no viaja al portal, que la firma de nuevo con su llave.</summary>
    private static string HuellaDeIp(HttpContext http)
    {
        var ip = Public.PublicManualCaptureEndpoints.ResolveClientIp(http) ?? "desconocida";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
    }

    /// <summary>El rechazo del portal como <c>{ code, message }</c> con sus extras (<c>totalActualCop</c>) y <c>Retry-After</c> en el 429.</summary>
    private static IResult Rechazo(HttpContext http, CajaPortalRechazoException ex)
    {
        var cuerpo = new Dictionary<string, object?> { ["code"] = ex.Code, ["message"] = ex.Message };
        foreach (var (clave, valor) in ex.Extras)
        {
            if (clave == "retryAfterSegundos")
            {
                if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos))
                    http.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
                continue;
            }

            cuerpo[clave] = long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) ? numero : valor;
        }

        return Results.Json(cuerpo, JsonOptions, statusCode: ex.EstadoHttp);
    }

    private static IResult PortalNoDisponible() =>
        Error(StatusCodes.Status503ServiceUnavailable, CodigoPortalNoDisponible, "La caja no está disponible por ahora. Intente de nuevo en unos minutos.");

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { code, message }, JsonOptions, statusCode: status);
}
