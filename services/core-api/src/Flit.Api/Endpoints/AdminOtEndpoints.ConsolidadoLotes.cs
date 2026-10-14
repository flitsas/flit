using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Api.Endpoints.Tramites;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Épica #13216 (HU #13391, ADR-0070 adenda v4) — <c>POST /api/v1/admin/ot/consolidados/lotes</c>: el lote de descarga
/// masiva de consolidados MAESTROS desde la bandeja del OT. Contrato: <c>CrearLoteConsolidadosMaestrosOt</c> en
/// <c>contracts/openapi/core-api.v1.yaml</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Autorización: <c>OtModulePolicy</c> del grupo AND <c>consolidado-masivo.download</c> (D-FB4).</item>
///   <item>Tenant: el del claim, como el resto de <see cref="AdminOtEndpoints"/> (la ruta NO está en
///   <c>RuntimeScopedRoutes</c>). Super Admin: <c>?transitOfficeId</c> obligatorio (400
///   <see cref="TransitOfficeRequerido"/>) y el tenant es el OT DUEÑO del organismo vía
///   <see cref="ResolveOtUserScopeAsync"/> (D-FB6). El <c>ot_admin</c> ignora el parámetro.</item>
///   <item>Organismo del lote: el MISMO mecanismo que la bandeja
///   (<see cref="IOtClientProcedureRepository.ResolveTransitOfficeIdAsync"/>); sin organismo resoluble, 403
///   <see cref="SinOrganismo"/>.</item>
///   <item>Selección y errores: los de la ruta de trámites (#13374, <see cref="ConsolidadoLoteEndpoints"/>): cuatro
///   claves, ProblemDetails con <c>error</c> en la raíz, mismo DTO de respuesta. El filtro es el cuerpo de
///   <c>POST /client-procedures/search</c>, con sus mismas validaciones (condición o familia fuera de catálogo ⇒
///   400 <c>filtro_invalido</c>) antes de tocar el motor.</item>
/// </list>
/// Uso de ejemplo:
/// <code>
/// POST /api/v1/admin/ot/consolidados/lotes[?transitOfficeId=…]
/// { "tipoDocumento": "consolidado_maestro", "confirmaEfectos": true,
///   "seleccion": { "modo": "filtro", "ids": [], "excluidos": ["…"], "filtro": { "condiciones": [ … ] } } }
/// → 202 LoteConsolidados { "estado": "en_cola", "tipoDocumento": "consolidado_maestro", "total": 248 }
/// </code>
/// </remarks>
public static partial class AdminOtEndpoints
{
    /// <summary>Ruta de creación del lote OT (bajo el grupo <c>/api/v1/admin/ot</c>).</summary>
    internal const string RutaLotesOt = "/consolidados/lotes";

    /// <summary>400: Super Admin sin <c>?transitOfficeId</c> (D-FB6, AC4).</summary>
    internal const string TransitOfficeRequerido = "transit_office_requerido";

    /// <summary>400: <c>?transitOfficeId</c> del Super Admin fuera del catálogo de organismos.</summary>
    internal const string TransitOfficeInvalido = "transit_office_invalido";

    /// <summary>403: el tenant OT no tiene un organismo resoluble (sin perfil OT).</summary>
    internal const string SinOrganismo = "sin_organismo";

    private static async Task<IResult> CrearLoteConsolidadosOtAsync(
        HttpContext http,
        [FromBody] CrearLoteConsolidadosOtBody? body,
        [FromQuery] Guid? transitOfficeId,
        CrearLoteConsolidadosHandler handler,
        LoteSeleccionResolverPorOrigen resolvers,
        IOtClientProcedureRepository bandeja,
        ITransitOfficeCatalog transitOfficeCatalog,
        FlitDbContext db,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var logger = loggers.CreateLogger(typeof(AdminOtEndpoints).FullName!);

        if (ConsolidadoLoteEndpoints.UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        // D-FB6 — el Super Admin opera la bandeja de UN organismo: sin él no hay universo que resolver.
        var isSuperAdmin = IsSuperAdmin(http.User);
        Guid? organismoElegido = null;
        if (isSuperAdmin)
        {
            if (transitOfficeId is not { } elegido || elegido == Guid.Empty)
                return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status400BadRequest, TransitOfficeRequerido,
                    "Indique el organismo de tránsito (transitOfficeId) cuya bandeja quiere descargar.");
            if (!transitOfficeCatalog.Exists(elegido))
                return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status400BadRequest, TransitOfficeInvalido,
                    "El organismo de tránsito indicado no existe en el catálogo.");
            organismoElegido = elegido;
        }

        // Tenant del claim; Super Admin → el tenant OT dueño del organismo (404 si no hay ninguno vinculado).
        var (otTenantId, scopeError) = await ResolveOtUserScopeAsync(http.User, organismoElegido, db, ct).ConfigureAwait(false);
        if (scopeError is not null)
            return scopeError;

        var organismo = await bandeja.ResolveTransitOfficeIdAsync(otTenantId, organismoElegido, ct).ConfigureAwait(false);
        if (organismo is not { } otTransitOfficeId || otTransitOfficeId == Guid.Empty)
        {
            LogLoteOtSinOrganismo(logger, usuarioId);
            return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status403Forbidden, SinOrganismo,
                "El usuario no tiene un organismo de tránsito activo.");
        }

        if (!resolvers.Atiende(ConsolidadoExportOrigin.OtBandeja))
        {
            LogLoteOtSinResolver(logger, usuarioId);
            return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status503ServiceUnavailable,
                CrearLoteConsolidadosErrores.MotorInactivo, CrearLoteConsolidadosHandler.MensajeNoDisponible);
        }

        var cuerpoSeleccion = body?.Seleccion;
        if (!ConsolidadoLoteEndpoints.TryMapearSeleccion(
                cuerpoSeleccion?.Modo, cuerpoSeleccion?.Ids, cuerpoSeleccion?.Excluidos, cuerpoSeleccion?.Filtro,
                f => new OtBandejaLoteFiltro(f.ToFilter()), cuerpoSeleccion is null, out var seleccion))
            return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status400BadRequest, ConsolidadoLoteEndpoints.SeleccionInvalida,
                "La selección debe ser modo «ids» con su lista, o modo «filtro» con el filtro de la bandeja.");

        // Mismas validaciones que POST /client-procedures/search: ignorar un campo o una familia fuera de catálogo
        // congelaría MÁS trámites de los que el usuario filtró.
        if (seleccion is SeleccionPorFiltro && cuerpoSeleccion!.Filtro is { } filtro
            && (OtBandejaQueryConditions.Validate(filtro.Condiciones) ?? filtro.ValidarFamilia()) is { } problema)
            return ConsolidadoLoteEndpoints.Problema(StatusCodes.Status400BadRequest,
                LoteSeleccionInvalidaException.CodigoFiltroInvalido, problema);

        var command = new CrearLoteConsolidadosCommand
        {
            Origen = ConsolidadoExportOrigin.OtBandeja,
            UsuarioId = usuarioId,
            RolCodigo = ConsolidadoLoteEndpoints.RolDelSolicitante(http.User, isSuperAdmin),
            TenantId = otTenantId,
            OtTransitOfficeId = otTransitOfficeId,
            TipoDocumento = string.IsNullOrWhiteSpace(body?.TipoDocumento) ? null : body.TipoDocumento.Trim(),
            Seleccion = seleccion,
            ConfirmaEfectos = body?.ConfirmaEfectos,
            ClientIp = http.Connection.RemoteIpAddress,
            UserAgent = ConsolidadoLoteEndpoints.UserAgent(http),
        };

        var resultado = await handler.HandleAsync(command, ct).ConfigureAwait(false);
        return resultado.Creado
            ? Results.Accepted($"{ConsolidadoLoteEndpoints.RutaLotes}/{resultado.Lote!.Id}", LoteConsolidadosDto.Desde(resultado.Lote))
            : ConsolidadoLoteEndpoints.ProblemaDe(resultado);
    }

    // Logs sin PII: solo el id del usuario.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote de consolidados OT rechazado: el tenant del usuario {UsuarioId} no tiene organismo resoluble.")]
    private static partial void LogLoteOtSinOrganismo(ILogger logger, Guid usuarioId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote de consolidados OT rechazado: no hay resolver de selección ot_bandeja (usuario {UsuarioId}).")]
    private static partial void LogLoteOtSinResolver(ILogger logger, Guid usuarioId);
}

/// <summary>
/// Cuerpo de <c>POST /api/v1/admin/ot/consolidados/lotes</c> (<c>CrearLoteConsolidadosOtRequest</c>). Sin origen,
/// tenant ni organismo: los decide el servidor.
/// </summary>
internal sealed record CrearLoteConsolidadosOtBody
{
    /// <summary>Solo <c>consolidado_maestro</c>; <c>null</c> = ese. Otro valor ⇒ 400 <c>tipo_no_permitido</c>.</summary>
    public string? TipoDocumento { get; init; }

    /// <summary>CF-08: debe ser <c>true</c>.</summary>
    public bool? ConfirmaEfectos { get; init; }

    public LoteSeleccionOtBody? Seleccion { get; init; }
}

/// <summary>
/// Selección de la bandeja OT tal como la arma el frontend (#13394): siempre las cuatro claves; se usan las del modo
/// y se ignoran las demás (mismo criterio que <see cref="LoteSeleccionBody"/>).
/// </summary>
internal sealed record LoteSeleccionOtBody
{
    /// <summary><c>ids</c> | <c>filtro</c>.</summary>
    public string? Modo { get; init; }

    /// <summary>Solo modo <c>ids</c> (máximo 10.000).</summary>
    public IReadOnlyList<Guid>? Ids { get; init; }

    /// <summary>Solo modo <c>filtro</c> (máximo 10.000).</summary>
    public IReadOnlyList<Guid>? Excluidos { get; init; }

    /// <summary>Solo modo <c>filtro</c>: el cuerpo de <c>POST /client-procedures/search</c>; la página se ignora.</summary>
    public OtBandejaSearchRequest? Filtro { get; init; }
}
