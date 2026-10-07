using System.Security.Claims;
using System.Text.Json.Serialization;
using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Épica #13216 (HU #13374, ADR-0070 D7) — motor de lotes de descarga masiva de consolidados. Esta HU publica
/// solo el <c>POST</c> de creación; los <c>GET</c> (actual, por id, parte) los añade #13379 en este mismo archivo
/// y la cancelación #13307. Contrato: <c>contracts/openapi/core-api.v1.yaml</c> (<c>CrearLoteConsolidados</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Permiso <see cref="ConsolidadoLotePermisos.Descargar"/> (Super Admin pasa por el bypass del handler).</item>
///   <item>Tenant y origen: de <see cref="RequestTenantResolver.FromItems"/> (ruta en
///   <c>TenantEnforcementMiddleware.RuntimeScopedRoutes</c>), NUNCA del cuerpo (A4.1). Usuario de compañía →
///   origen <c>tramites</c> con el tenant del JWT (el <c>X-Tenant-Id</c> del cliente se acepta, pero el middleware
///   lo reemplaza por el del token); Super Admin → origen <c>superadmin</c>, sin compañía, con el
///   <c>X-Tenant-Id</c> opcional como scope.</item>
///   <item>Errores: ProblemDetails con el código estable en <c>error</c> (y <c>loteActivoId</c> en el 409), en la
///   raíz del cuerpo.</item>
/// </list>
/// Uso de ejemplo:
/// <code>
/// POST /api/v1/tramites/consolidados/lotes
/// { "tipoDocumento": "consolidado", "confirmaEfectos": true,
///   "seleccion": { "modo": "ids", "ids": ["…"], "excluidos": [], "filtro": null } }
/// → 202 LoteConsolidados { "estado": "en_cola", "total": 1, "partes": [] }
/// </code>
/// </remarks>
internal static partial class ConsolidadoLoteEndpoints
{
    /// <summary>Ruta de creación (bajo el runtime de trámites: tenant del JWT).</summary>
    public const string RutaCrear = "/api/v1/tramites/consolidados/lotes";

    /// <summary>Prefijo neutro de lectura, descarga y cancelación (#13379 / #13307).</summary>
    public const string RutaLotes = "/api/v1/consolidados/lotes";

    /// <summary>400: <c>modo</c> desconocido, o modo <c>filtro</c> sin <c>filtro</c>.</summary>
    public const string SeleccionInvalida = "seleccion_invalida";

    /// <summary>422: el atajo de búsqueda rápida abarca más borradores de los que se evalúan (mismo caso que el listado).</summary>
    public const string BusquedaDemasiadoAmplia = "busqueda_demasiado_amplia";

    private const int MaxUserAgent = 512;

    internal static IEndpointRouteBuilder MapConsolidadoLoteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(RutaCrear, CrearAsync)
            .RequirePermission(ConsolidadoLotePermisos.Descargar)
            .WithName("CrearLoteConsolidados")
            .WithSummary("Crea un lote de descarga masiva de consolidados")
            .Produces<LoteConsolidadosDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> CrearAsync(
        HttpContext http,
        [FromBody] CrearLoteConsolidadosBody? body,
        CrearLoteConsolidadosHandler handler,
        LoteSeleccionResolverPorOrigen resolvers,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(ConsolidadoLoteEndpoints).FullName!);

        if (UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        // A4.1 / AC6 — el origen lo decide el middleware (IsSuperAdmin), nunca el cuerpo: un campo
        // `origen`/`origin` en el JSON no existe en el contrato y el binding lo descarta.
        var (tenantId, isSuperAdmin) = RequestTenantResolver.FromItems(http);
        var origen = isSuperAdmin ? ConsolidadoExportOrigin.Superadmin : ConsolidadoExportOrigin.Tramites;

        // Falla cerrado: los dos orígenes tienen resolver (#13370 tramites, #13383 superadmin); si una composición
        // lo omitiera, no se crea nada (503, el frontend ofrece reintentar) en vez de lanzar.
        if (!resolvers.Atiende(origen))
        {
            LogOrigenSinResolver(logger, origen, usuarioId);
            return Problema(StatusCodes.Status503ServiceUnavailable, CrearLoteConsolidadosErrores.MotorInactivo,
                CrearLoteConsolidadosHandler.MensajeNoDisponible);
        }

        if (!TryMapearSeleccion(body?.Seleccion, out var seleccion))
            return Problema(StatusCodes.Status400BadRequest, SeleccionInvalida,
                "La selección debe ser modo «ids» con su lista, o modo «filtro» con el filtro del listado.");

        // Desviación del frontend #2 — `alcanceRed` (vista de red) se acepta en el contrato pero el motor aún no
        // lo aplica: el lote se resuelve con el alcance propio del token. Hallazgo reportado en HU #13374.
        if (!string.IsNullOrWhiteSpace(body?.Seleccion?.Filtro?.AlcanceRed))
            LogAlcanceRedIgnorado(logger, usuarioId);

        var command = new CrearLoteConsolidadosCommand
        {
            Origen = origen,
            UsuarioId = usuarioId,
            RolCodigo = RolDelSolicitante(http.User, isSuperAdmin),
            TenantId = isSuperAdmin ? null : tenantId,
            ScopeTenantId = isSuperAdmin ? tenantId : null,
            TipoDocumento = string.IsNullOrWhiteSpace(body?.TipoDocumento) ? null : body.TipoDocumento.Trim(),
            Seleccion = seleccion,
            ConfirmaEfectos = body?.ConfirmaEfectos,
            ClientIp = http.Connection.RemoteIpAddress,
            UserAgent = UserAgent(http),
        };

        CrearLoteConsolidadosResultado resultado;
        try
        {
            resultado = await handler.HandleAsync(command, ct);
        }
        catch (BusquedaRapidaDemasiadoAmpliaException ex)
        {
            // Mismo caso que el listado (422): mejor pedir que acote que congelar un conjunto truncado.
            return Problema(StatusCodes.Status422UnprocessableEntity, BusquedaDemasiadoAmplia, ex.Message);
        }

        return resultado.Creado
            ? Results.Accepted($"{RutaLotes}/{resultado.Lote!.Id}", LoteConsolidadosDto.Desde(resultado.Lote))
            : ProblemaDe(resultado);
    }

    /// <summary>Traduce el código estable del caso de uso a HTTP (contrato §5).</summary>
    internal static IResult ProblemaDe(CrearLoteConsolidadosResultado r)
    {
        var status = r.Error switch
        {
            CrearLoteConsolidadosErrores.ConfirmacionRequerida
                or CrearLoteConsolidadosErrores.TipoNoPermitido
                or CrearLoteConsolidadosErrores.SeleccionRequerida
                // Igual que POST /instances/search: un campo o atajo fuera de catálogo es 400, no 422.
                or LoteSeleccionInvalidaException.CodigoFiltroInvalido => StatusCodes.Status400BadRequest,
            CrearLoteConsolidadosErrores.SinCompania => StatusCodes.Status403Forbidden,
            CrearLoteConsolidadosErrores.LoteActivo => StatusCodes.Status409Conflict,
            LoteSeleccionInvalidaException.CodigoExcedeTope => StatusCodes.Status422UnprocessableEntity,
            CrearLoteConsolidadosErrores.MotorInactivo
                or CrearLoteConsolidadosErrores.LoteNoCreado => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Problema(status, r.Error ?? "error_desconocido", r.Mensaje, r.LoteActivoId, r.Total, r.Tope);
    }

    /// <summary>
    /// ProblemDetails del motor con el código estable en <c>error</c> (raíz). Compartido con la ruta OT (#13391).
    /// M1: <paramref name="total"/> y <paramref name="tope"/> solo en el 422 <c>seleccion_excede_tope</c> por tope total.
    /// </summary>
    internal static IResult Problema(
        int status, string error, string? detail, Guid? loteActivoId = null, int? total = null, int? tope = null)
    {
        var extensions = new Dictionary<string, object?> { ["error"] = error };
        if (loteActivoId is { } id)
            extensions["loteActivoId"] = id;
        if (total is { } t)
            extensions["total"] = t;
        if (tope is { } max)
            extensions["tope"] = max;
        return Results.Problem(statusCode: status, title: error, detail: detail, extensions: extensions);
    }

    /// <summary>
    /// Desviación del frontend #1 — la selección llega SIEMPRE con <c>{modo, ids, excluidos, filtro}</c>: se usan
    /// las claves del modo y se ignoran las demás (sin rechazarlas). <c>null</c> se pasa tal cual: el caso de uso
    /// responde <c>seleccion_requerida</c>.
    /// </summary>
    internal static bool TryMapearSeleccion(LoteSeleccionBody? body, out LoteSeleccion? seleccion) =>
        // Tenant y usuario del filtro los pone el resolver desde el token; skip/take se ignoran.
        TryMapearSeleccion(body?.Modo, body?.Ids, body?.Excluidos, body?.Filtro,
            f => new TramitesLoteFiltro(f.ToRequest(tenantId: null)), body is null, out seleccion);

    /// <summary>
    /// Mapeo común de la selección de cuatro claves a <see cref="LoteSeleccion"/>; cada ruta aporta cómo su filtro
    /// se convierte en <see cref="LoteFiltro"/> (#13374 listado de trámites, #13391 bandeja OT).
    /// <paramref name="sinSeleccion"/> = el cuerpo no trajo <c>seleccion</c>: se devuelve <c>null</c> y el caso de
    /// uso responde <c>seleccion_requerida</c>.
    /// </summary>
    internal static bool TryMapearSeleccion<TFiltro>(
        string? modo,
        IReadOnlyList<Guid>? ids,
        IReadOnlyList<Guid>? excluidos,
        TFiltro? filtro,
        Func<TFiltro, LoteFiltro> aFiltro,
        bool sinSeleccion,
        out LoteSeleccion? seleccion)
        where TFiltro : class
    {
        ArgumentNullException.ThrowIfNull(aFiltro);
        seleccion = null;
        if (sinSeleccion)
            return true;

        switch (modo?.Trim().ToLowerInvariant())
        {
            case ConsolidadoExportSelectionMode.Ids:
                seleccion = new SeleccionPorIds(ids ?? []);
                return true;
            case ConsolidadoExportSelectionMode.Filtro when filtro is not null:
                seleccion = new SeleccionPorFiltro(aFiltro(filtro), excluidos ?? []);
                return true;
            default:
                return false;
        }
    }

    internal static Guid? UsuarioDelToken(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Rol con el que se pide (auditoría y revalidación CF-16). Super Admin → <c>SuperAdmin</c>; usuario multi-rol →
    /// sus roles distintos en orden ordinal, separados por coma (el JWT no dice cuál «usó»).
    /// </summary>
    internal static string RolDelSolicitante(ClaimsPrincipal user, bool isSuperAdmin)
    {
        if (isSuperAdmin)
            return AdminAuthorization.SuperAdminRole;
        var roles = RequestTenantResolver.RoleValues(user)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        return roles.Count == 0 ? "sin_rol" : string.Join(",", roles);
    }

    internal static string? UserAgent(HttpContext http)
    {
        var ua = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(ua))
            return null;
        return ua.Length <= MaxUserAgent ? ua : ua[..MaxUserAgent];
    }

    // Logs sin PII: solo ids y el origen.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote de consolidados rechazado: no hay resolver de selección para el origen {Origen} (usuario {UsuarioId}).")]
    private static partial void LogOrigenSinResolver(ILogger logger, string origen, Guid usuarioId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote de consolidados: alcanceRed recibido e ignorado; se resuelve con el alcance propio (usuario {UsuarioId}).")]
    private static partial void LogAlcanceRedIgnorado(ILogger logger, Guid usuarioId);
}

/// <summary>Cuerpo de <c>POST /api/v1/tramites/consolidados/lotes</c> (<c>CrearLoteConsolidadosRequest</c>). Sin origen ni tenant.</summary>
internal sealed record CrearLoteConsolidadosBody
{
    /// <summary><c>consolidado</c> | <c>consolidado_maestro</c>; <c>null</c> = el del origen.</summary>
    public string? TipoDocumento { get; init; }

    /// <summary>CF-08: debe ser <c>true</c>.</summary>
    public bool? ConfirmaEfectos { get; init; }

    public LoteSeleccionBody? Seleccion { get; init; }
}

/// <summary>Selección tal como la arma el frontend (<c>useSeleccionLote</c>): siempre las cuatro claves.</summary>
internal sealed record LoteSeleccionBody
{
    /// <summary><c>ids</c> | <c>filtro</c>.</summary>
    public string? Modo { get; init; }

    /// <summary>Solo modo <c>ids</c> (máximo 10.000); en modo <c>filtro</c> se ignora.</summary>
    public IReadOnlyList<Guid>? Ids { get; init; }

    /// <summary>Solo modo <c>filtro</c> (máximo 10.000); en modo <c>ids</c> se ignora.</summary>
    public IReadOnlyList<Guid>? Excluidos { get; init; }

    /// <summary>Solo modo <c>filtro</c>: el cuerpo de <c>POST /instances/search</c> (sin paginar) más <c>alcanceRed</c>.</summary>
    public LoteFiltroTramitesBody? Filtro { get; init; }
}

/// <summary>
/// Filtro del listado de <c>/tramites</c> para el lote: <see cref="TramitesSearchRequest"/> (mismo mapeo que el
/// listado) más <see cref="AlcanceRed"/>. <c>skip</c>/<c>take</c>/<c>sortBy</c>/<c>sortDir</c> se aceptan y la
/// paginación se ignora.
/// </summary>
internal sealed record LoteFiltroTramitesBody : TramitesSearchRequest
{
    /// <summary><c>null</c> = alcance propio; <c>red</c> o un <c>childTenantId</c> = vista de red (aún no aplicada por el motor).</summary>
    public string? AlcanceRed { get; init; }
}

/// <summary>Respuesta <c>LoteConsolidados</c> del contrato (202 de la creación; #13379 la reutiliza en los GET).</summary>
internal sealed record LoteConsolidadosDto(
    Guid Id,
    string Estado,
    string TipoDocumento,
    int Total,
    int Procesados,
    int Incluidos,
    int Omitidos,
    int Generados,
    DateTimeOffset CreadoEn,
    DateTimeOffset? TerminadoEn,
    DateTimeOffset? ExpiraEn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NombreBase,
    IReadOnlyList<ParteLoteConsolidadosDto> Partes)
{
    /// <summary>Proyección del lote. <paramref name="partes"/> vacío hasta que el lote termina (CF-09).</summary>
    public static LoteConsolidadosDto Desde(
        ConsolidadoExportBatch lote, IReadOnlyList<ParteLoteConsolidadosDto>? partes = null, string? nombreBase = null)
    {
        ArgumentNullException.ThrowIfNull(lote);
        return new LoteConsolidadosDto(
            lote.Id,
            lote.Status,
            lote.DocumentType,
            lote.TotalItems,
            lote.IncludedCount + lote.OmittedCount,
            lote.IncludedCount,
            lote.OmittedCount,
            lote.GeneratedCount,
            lote.CreatedAt,
            lote.FinishedAt,
            lote.ExpiresAt,
            nombreBase,
            partes ?? []);
    }
}

/// <summary>Una parte descargable del lote (<c>LoteConsolidados.partes[]</c>).</summary>
internal sealed record ParteLoteConsolidadosDto(int Numero, string NombreArchivo, int Pdfs, int Omitidos, long Bytes);
