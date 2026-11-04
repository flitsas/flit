using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>Configuración del portal de Persona Natural (HU #14072): parámetros, organismos y lista blanca de tipos.</summary>
public static partial class AdminPlataformaPersonaNaturalEndpoints
{
    /// <summary>Cuerpo del guardado de parámetros: nulo en un valor = predeterminado del BFF. <c>RowVersion</c> es la versión que se leyó.</summary>
    public sealed record GuardarParametrosRequest(
        int? SesionVigenciaMinutos,
        int? BorradorVigenciaDias,
        int? TopeConsultasCiudadanoDia,
        int? TopeConsultasGlobalDia,
        bool? CaptchaActivo,
        string? SoporteCorreo,
        string? SoporteTelefono,
        string? SoporteHorario,
        long? RowVersion);

    public sealed record HabilitarRequest(bool? Habilitado);

    public sealed record CanalPuntoRequest(bool? Encendido);

    private static void MapConfiguracion(RouteGroupBuilder group)
    {
        group.MapGet("/configuracion/parametros", ObtenerParametrosAsync)
            .WithName("AdminPnParametrosGet")
            .WithSummary("Parámetros generales del portal (nulo = predeterminado del BFF) con su versión")
            .Produces<ParametrosPn>(StatusCodes.Status200OK);

        group.MapPut("/configuracion/parametros", GuardarParametrosAsync)
            .WithName("AdminPnParametrosPut")
            .WithSummary("Guarda los parámetros generales con control de concurrencia (409 si la versión está desactualizada)")
            .Produces<ParametrosPn>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/organismos", ListarOrganismosAsync)
            .WithName("AdminPnOrganismosList")
            .WithSummary("Organismos de tránsito con su estado para el portal y los tipos con tarifa vigente")
            .Produces<IReadOnlyList<OrganismoPnView>>(StatusCodes.Status200OK);

        group.MapPut("/organismos/{organismoId:guid}", EstablecerOrganismoAsync)
            .WithName("AdminPnOrganismoSet")
            .WithSummary("Habilita o deshabilita un organismo para la empresa Persona Natural")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/organismos/{organismoId:guid}/canal-punto", EstablecerCanalPuntoAsync)
            .WithName("AdminPnCanalPuntoSet")
            .WithSummary("Enciende o apaga el canal del punto de atención sin cambiar el grant del portal")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/tipos", ListarTiposAsync)
            .WithName("AdminPnTiposList")
            .WithSummary("Tipos de trámite candidatos para el portal (elegibles) y su habilitación")
            .Produces<IReadOnlyList<TipoPnView>>(StatusCodes.Status200OK);

        group.MapPut("/tipos/{tipoTramiteId:guid}", EstablecerTipoAsync)
            .WithName("AdminPnTipoSet")
            .WithSummary("Habilita (solo si es elegible) o deshabilita un tipo de trámite para el portal")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> ObtenerParametrosAsync(ParametrosPnHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ObtenerAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> GuardarParametrosAsync(
        GuardarParametrosRequest body, ParametrosPnHandler handler, HttpContext http, CancellationToken ct)
    {
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
        if (body.RowVersion is not { } version)
            return Error(StatusCodes.Status400BadRequest, "row_version_obligatorio", "Indique la versión de la configuración que está editando.");

        var r = await handler.GuardarAsync(
            new GuardarParametrosPnCommand(
                body.SesionVigenciaMinutos, body.BorradorVigenciaDias, body.TopeConsultasCiudadanoDia, body.TopeConsultasGlobalDia,
                body.CaptchaActivo, body.SoporteCorreo, body.SoporteTelefono, body.SoporteHorario, version, actor),
            ct).ConfigureAwait(false);

        return r.Error switch
        {
            null => Results.Ok(r.Parametros),
            ConfiguracionPnErrores.Conflicto => Results.Json(
                new { error = r.Error, message = r.Mensaje, actual = r.Parametros }, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(new { error = r.Error, campo = r.Campo, message = r.Mensaje }, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> ListarOrganismosAsync(
        OrganismosPnHandler handler, CancellationToken ct, [FromQuery] bool? soloHabilitados = null) =>
        Results.Ok(await handler.ListarAsync(soloHabilitados ?? false, ct).ConfigureAwait(false));

    private static async Task<IResult> EstablecerOrganismoAsync(
        Guid organismoId, HabilitarRequest body, OrganismosPnHandler handler, HttpContext http, CancellationToken ct)
    {
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
        if (body.Habilitado is not { } habilitado)
            return Error(StatusCodes.Status400BadRequest, "habilitado_obligatorio", "Indique si el organismo queda habilitado o no.");

        return await handler.EstablecerAsync(organismoId, habilitado, actor, ct).ConfigureAwait(false) switch
        {
            null => Results.NoContent(),
            ConfiguracionPnErrores.OrganismoNoOperable => Error(
                StatusCodes.Status422UnprocessableEntity, ConfiguracionPnErrores.OrganismoNoOperable,
                "El organismo no está activo en FLIT: no tiene perfil de organismo con empresa activa. Actívelo en FLIT antes de habilitarlo."),
            _ => Error(StatusCodes.Status404NotFound, ConfiguracionPnErrores.OrganismoNoExiste, "El organismo de tránsito no existe o está inactivo."),
        };
    }

    private static async Task<IResult> EstablecerCanalPuntoAsync(
        Guid organismoId, CanalPuntoRequest body, CanalPuntoPnHandler handler, HttpContext http, CancellationToken ct)
    {
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
        if (body.Encendido is not { } encendido)
            return Error(StatusCodes.Status400BadRequest, "encendido_obligatorio", "Indique si el canal del punto queda encendido o no.");

        return await handler.EstablecerAsync(organismoId, encendido, actor, ct).ConfigureAwait(false) switch
        {
            null => Results.NoContent(),
            ConfiguracionPnErrores.OrganismoNoOperable => Error(
                StatusCodes.Status422UnprocessableEntity, ConfiguracionPnErrores.OrganismoNoOperable,
                "El organismo no está activo en FLIT: no tiene perfil de organismo con empresa activa. Actívelo en FLIT antes de habilitarlo."),
            _ => Error(StatusCodes.Status404NotFound, ConfiguracionPnErrores.OrganismoNoExiste, "El organismo de tránsito no existe o está inactivo."),
        };
    }

    private static async Task<IResult> ListarTiposAsync(TiposPnHandler handler, CancellationToken ct) =>
        Results.Ok(await handler.ListarAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> EstablecerTipoAsync(
        Guid tipoTramiteId, HabilitarRequest body, TiposPnHandler handler, HttpContext http, CancellationToken ct)
    {
        if (AdminCompaniesDomainEndpoints.ResolveUserId(http.User) is not { } actor)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");
        if (body.Habilitado is not { } habilitado)
            return Error(StatusCodes.Status400BadRequest, "habilitado_obligatorio", "Indique si el tipo queda habilitado o no.");

        return await handler.EstablecerAsync(tipoTramiteId, habilitado, actor, ct).ConfigureAwait(false) switch
        {
            null => Results.NoContent(),
            ConfiguracionPnErrores.TipoNoExiste => Error(StatusCodes.Status404NotFound, ConfiguracionPnErrores.TipoNoExiste, "El tipo de trámite no existe."),
            _ => Error(
                StatusCodes.Status422UnprocessableEntity, ConfiguracionPnErrores.TipoNoElegible,
                "Solo se pueden habilitar tipos elegibles para persona natural con el asistente encendido en FLIT 2."),
        };
    }
}
