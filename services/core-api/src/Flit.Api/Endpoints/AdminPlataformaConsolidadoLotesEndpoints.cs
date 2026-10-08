using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Tramites;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13420 (épica #13216) — Super Admin → Plataforma → Descarga masiva: ver y editar los parámetros del motor de
/// lotes (<c>tramites.consolidado_export_settings</c>, fila única global de plataforma, sin tenant). Mismo grupo y
/// política que <see cref="AdminPlataformaNotificacionesEndpoints"/> (<see cref="AdminAuthorization.SuperAdminPolicy"/>:
/// rol <c>SuperAdmin</c> activo en el token; cualquier otro rol ⇒ 403).
/// <list type="bullet">
///   <item><c>GET</c> ⇒ 200 con todos los campos, quién y cuándo cambió, <c>rowVersion</c> y los <c>limites</c> del
///   DDL 133; 404 <c>parametros_no_encontrados</c> si la fila no existe (no se inventan valores: sin fila el alta ya
///   responde 503 <c>motor_inactivo</c>).</item>
///   <item><c>PUT</c> ⇒ 200 con la fila releída; 400 ValidationProblem <c>parametros_invalidos</c> con <c>errors</c>
///   por campo (camelCase); 409 <c>row_version_conflict</c>; 404 como el GET; 401 <c>usuario_no_identificado</c> si el
///   token no trae un usuario (<c>sub</c> / <c>NameIdentifier</c>), sin escribir (nunca <c>updated_by = NULL</c>).</item>
/// </list>
/// Errores en ProblemDetails con el código estable en <c>error</c> (como <c>ConsolidadoLoteEndpoints.Problema</c>).
/// </summary>
public static class AdminPlataformaConsolidadoLotesEndpoints
{
    public const string RutaParametros = "/api/v1/admin/plataforma/consolidados/lotes/parametros";

    /// <summary>400: algún campo ausente, fuera de rango o un lease ≤ su timeout.</summary>
    public const string ParametrosInvalidos = "parametros_invalidos";

    /// <summary>404: no existe la fila única de parámetros.</summary>
    public const string ParametrosNoEncontrados = "parametros_no_encontrados";

    /// <summary>409: la fila cambió después de leerla (otro Super Admin guardó antes).</summary>
    public const string RowVersionConflict = "row_version_conflict";

    /// <summary>401: el token no identifica al usuario (sin <c>sub</c> ni <c>NameIdentifier</c>); no se escribe nada.</summary>
    public const string UsuarioNoIdentificado = "usuario_no_identificado";

    public static IEndpointRouteBuilder MapAdminPlataformaConsolidadoLotesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/consolidados/lotes")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Descarga masiva");

        group.MapGet("/parametros", GetAsync)
            .WithName("AdminPlataformaConsolidadoLotesGetParametros")
            .WithSummary("Parámetros vigentes del motor de descarga masiva de consolidados")
            .Produces<ParametrosMotorLoteDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/parametros", PutAsync)
            .WithName("AdminPlataformaConsolidadoLotesPutParametros")
            .WithSummary("Actualiza los parámetros del motor (concurrencia optimista por rowVersion)")
            .Produces<ParametrosMotorLoteDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] ObtenerParametrosMotorLoteHandler handler,
        CancellationToken ct)
    {
        var dto = await handler.HandleAsync(ct).ConfigureAwait(false);
        return dto is null ? NoEncontrados() : Results.Ok(dto);
    }

    private static async Task<IResult> PutAsync(
        [FromBody] ActualizarParametrosMotorLoteRequest? request,
        ClaimsPrincipal user,
        [FromServices] ActualizarParametrosMotorLoteHandler handler,
        CancellationToken ct)
    {
        // Code review Obs6 — sin usuario en el token no se guarda updated_by = NULL: 401 antes de validar o escribir.
        if (ConsolidadoLoteEndpoints.UsuarioDelToken(user) is not { } usuarioId)
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: UsuarioNoIdentificado,
                detail: "El token no identifica al usuario; vuelve a iniciar sesión.",
                extensions: new Dictionary<string, object?> { ["error"] = UsuarioNoIdentificado });

        if (request is null)
            return Invalidos(new Dictionary<string, string[]> { ["body"] = ["El cuerpo es obligatorio."] });

        var r = await handler.HandleAsync(
            new ActualizarParametrosMotorLoteCommand(
                request.MaxItemsPerBatch, request.MaxPdfsPerPart, request.MaxMbPerPart, request.ItemSlots,
                request.ItemTimeoutSeconds, request.ItemLeaseSeconds, request.MaxItemAttempts, request.RetryDelaySeconds,
                request.PartTimeoutSeconds, request.PartLeaseSeconds, request.MaxPartAttempts, request.RetentionHours,
                request.IsActive, request.RowVersion, usuarioId),
            ct).ConfigureAwait(false);

        return r.Estado switch
        {
            ParametrosMotorLoteEstado.Actualizado => Results.Ok(r.Parametros),
            ParametrosMotorLoteEstado.Invalido => Invalidos(r.Errores),
            ParametrosMotorLoteEstado.Conflicto => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: RowVersionConflict,
                detail: "Otro Super Admin guardó los parámetros después de que los cargaste. Recarga y vuelve a guardar.",
                extensions: new Dictionary<string, object?> { ["error"] = RowVersionConflict }),
            _ => NoEncontrados(),
        };
    }

    private static IResult Invalidos(IReadOnlyDictionary<string, string[]> errores) =>
        Results.ValidationProblem(
            errores.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
            detail: "Revisa los campos marcados.",
            title: ParametrosInvalidos,
            extensions: new Dictionary<string, object?> { ["error"] = ParametrosInvalidos });

    private static IResult NoEncontrados() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: ParametrosNoEncontrados,
            detail: "No existe la fila de parámetros del motor de descarga masiva.",
            extensions: new Dictionary<string, object?> { ["error"] = ParametrosNoEncontrados });
}

/// <summary>
/// HU #13420 — cuerpo de <c>PUT /api/v1/admin/plataforma/consolidados/lotes/parametros</c>. Todos obligatorios (un
/// nulo o ausente es 400 en su campo); <see cref="RowVersion"/> es el leído en el GET.
/// </summary>
public sealed record ActualizarParametrosMotorLoteRequest(
    int? MaxItemsPerBatch,
    int? MaxPdfsPerPart,
    int? MaxMbPerPart,
    int? ItemSlots,
    int? ItemTimeoutSeconds,
    int? ItemLeaseSeconds,
    int? MaxItemAttempts,
    int? RetryDelaySeconds,
    int? PartTimeoutSeconds,
    int? PartLeaseSeconds,
    int? MaxPartAttempts,
    int? RetentionHours,
    bool? IsActive,
    long? RowVersion);
