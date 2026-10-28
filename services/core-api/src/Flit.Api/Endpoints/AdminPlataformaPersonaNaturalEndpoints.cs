using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Administración del portal de Persona Natural en Plataforma (Feature J, Épica #14021, ADR-0075):
/// <c>/api/v1/admin/plataforma/persona-natural</c>. Exclusiva del Super Admin FLIT: policy por ROL más el permiso
/// <c>platform.persona_natural.manage</c> (401 sin sesión, 403 a cualquier otro rol o a un usuario sin el permiso). La
/// configuración es global (sin compañía): no lee <c>X-Tenant-Id</c>. Cada cambio queda en la auditoría de configuración.
/// <para>Este archivo: tarifas (HU #14071). Configuración (HU #14072) y contenido (HU #14073) son partes del mismo tipo.</para>
/// </summary>
public static partial class AdminPlataformaPersonaNaturalEndpoints
{
    public const string BasePath = "/api/v1/admin/plataforma/persona-natural";

    public sealed record VersionarTarifasRequest(Guid? TipoTramiteId, Guid? OrganismoId, DateOnly? VigenteDesde, IReadOnlyList<ValorTarifaRequest>? Valores);

    public sealed record ValorTarifaRequest(string? Concepto, long ValorCop);

    public static IEndpointRouteBuilder MapAdminPlataformaPersonaNaturalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup(BasePath)
            .WithTags("Admin · Plataforma · Persona Natural")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .RequirePermission(PersonaNaturalAdminAuthorization.ManageSlug);

        MapTarifas(group);
        MapConfiguracion(group);
        MapContenido(group);
        return app;
    }

    // ── Tarifas (HU #14071) ─────────────────────────────────────────────────────────────────────────────────────────

    private static void MapTarifas(RouteGroupBuilder group)
    {
        group.MapGet("/tarifas", ListarTarifasAsync)
            .WithName("AdminPnTarifasList")
            .WithSummary("Tarifas del portal: todas las versiones o solo las vigentes a una fecha")
            .Produces<IReadOnlyList<TarifaPnView>>(StatusCodes.Status200OK);

        group.MapGet("/tarifas/vigente", TarifaVigenteAsync)
            .WithName("AdminPnTarifaVigente")
            .WithSummary("Tarifa vigente de un tipo en un organismo (con la precedencia de la cotización)")
            .Produces<IReadOnlyList<TarifaPnView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/tarifas/{id:guid}/historial", HistorialTarifaAsync)
            .WithName("AdminPnTarifaHistorial")
            .WithSummary("Versiones de un ámbito (concepto, tipo y organismo), de la más reciente a la más antigua")
            .Produces<IReadOnlyList<TarifaPnView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/tarifas", VersionarTarifasAsync)
            .WithName("AdminPnTarifasVersionar")
            .WithSummary("Registra una versión nueva de tarifa; cierra la anterior el día anterior al inicio")
            .Produces<IReadOnlyList<TarifaPnView>>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListarTarifasAsync(
        ListarTarifasPnHandler handler,
        CancellationToken ct,
        [FromQuery] bool? soloVigentes = null,
        [FromQuery] Guid? tipoTramiteId = null,
        [FromQuery] Guid? organismoId = null,
        [FromQuery] string? concepto = null,
        [FromQuery] DateOnly? fecha = null) =>
        Results.Ok(await handler.HandleAsync(new TarifaPnFiltro(soloVigentes ?? false, tipoTramiteId, organismoId, concepto, fecha), ct).ConfigureAwait(false));

    private static async Task<IResult> TarifaVigenteAsync(
        TarifaVigentePnHandler handler,
        CancellationToken ct,
        [FromQuery] Guid? tipoTramiteId = null,
        [FromQuery] Guid? organismoId = null,
        [FromQuery] DateOnly? fecha = null)
    {
        if (tipoTramiteId is not { } tipo || organismoId is not { } organismo)
            return Error(StatusCodes.Status400BadRequest, "parametros_obligatorios", "Indique el tipo de trámite y el organismo.");
        return Results.Ok(await handler.HandleAsync(tipo, organismo, fecha, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> HistorialTarifaAsync(Guid id, HistorialTarifaPnHandler handler, CancellationToken ct)
    {
        var historial = await handler.HandleAsync(id, ct).ConfigureAwait(false);
        return historial is null
            ? Error(StatusCodes.Status404NotFound, TarifaPnErrores.NoEncontrada, "La tarifa no existe.")
            : Results.Ok(historial);
    }

    private static async Task<IResult> VersionarTarifasAsync(
        VersionarTarifasRequest body, VersionarTarifasPnHandler handler, HttpContext http, CancellationToken ct)
    {
        var actor = AdminCompaniesDomainEndpoints.ResolveUserId(http.User);
        if (actor is not { } usuario)
            return Error(StatusCodes.Status401Unauthorized, "usuario_no_identificado", "No se pudo identificar al usuario.");

        var resultado = await handler.HandleAsync(
            new VersionarTarifasPnCommand(
                body.TipoTramiteId, body.OrganismoId, body.Valores?.Select(v => new TarifaPnValor(v.Concepto ?? string.Empty, v.ValorCop)).ToList(), body.VigenteDesde, usuario),
            ct).ConfigureAwait(false);

        return resultado.Error switch
        {
            null => Results.Created($"{BasePath}/tarifas", resultado.Tarifas),
            TarifaPnErrores.TipoNoExiste or TarifaPnErrores.OrganismoNoExiste =>
                Error(StatusCodes.Status404NotFound, resultado.Error, "El tipo de trámite o el organismo no existe."),
            TarifaPnErrores.Solapada => Error(
                StatusCodes.Status409Conflict, resultado.Error,
                "Ya existe una tarifa para ese concepto, tipo y organismo que se cruza con esas fechas."),
            _ => Error(StatusCodes.Status400BadRequest, resultado.Error!, MensajeTarifa(resultado.Error!)),
        };
    }

    private static string MensajeTarifa(string error) => error switch
    {
        TarifaPnErrores.ValoresVacios => "Indique al menos un concepto con su valor.",
        TarifaPnErrores.ConceptoInvalido => $"Concepto inválido: use {string.Join(", ", ConceptosTarifaPn.Todos)}.",
        TarifaPnErrores.ConceptoRepetido => "Un concepto no puede repetirse en la misma versión.",
        TarifaPnErrores.ValorInvalido => $"Cada valor debe ser un entero en pesos entre 0 y {VersionarTarifasPnHandler.ValorMaximoCop}.",
        TarifaPnErrores.TipoObligatorio => "Derechos de tránsito y servicio FLIT se registran siempre para un tipo de trámite.",
        TarifaPnErrores.FechaInicioInvalida => "Indique la fecha de inicio de la vigencia.",
        TarifaPnErrores.FechaInicioPasada => "La fecha de inicio no puede ser anterior a hoy.",
        _ => "No se pudo registrar la tarifa.",
    };

    private static IResult Error(int status, string error, string message) =>
        Results.Json(new { error, message }, statusCode: status);
}
