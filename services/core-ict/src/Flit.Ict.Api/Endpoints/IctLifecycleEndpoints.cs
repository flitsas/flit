using Flit.Ict.Api.Authorization;
using Flit.Ict.Application.Status;

namespace Flit.Ict.Api.Endpoints;

/// <summary>
/// Ciclo de vida del trámite ya materializado (servicios v1 #10 pauseDraftProcess / #11 abortProcess).
/// Se conservan los PATHS v1 (<c>PUT /api/v1/external-transaction/{abortProcess|pauseDraftProcess}/{id}</c>)
/// y la forma de respuesta v1 <c>{ success, message, data }</c>. El <c>{id}</c> es el manager_id_transaction
/// (TransactionFlit), igual que en estado/reproceso. Alias de desarrollo <c>/api/ict/*</c>.
/// </summary>
public static class IctLifecycleEndpoints
{
    /// <summary>Body de abortProcess: solo <c>observation</c> (nota de la anulación).</summary>
    public sealed record AbortProcessRequest(string? Observation);

    /// <summary>Body de pauseDraftProcess: <c>pauseProcess</c> (true=pausar) + <c>observation</c>.</summary>
    public sealed record PauseProcessRequest(bool PauseProcess, string? Observation);

    /// <summary>Respuesta v1 UpdateResponseDto: { success, message, data }.</summary>
    private sealed record LifecycleV1Result(bool Success, string Message, object? Data);

    private static readonly string[] AbortPaths =
    [
        "/api/v1/external-transaction/abortProcess/{id}",
        "/api/ict/abortProcess/{id}",
    ];

    private static readonly string[] PausePaths =
    [
        "/api/v1/external-transaction/pauseDraftProcess/{id}",
        "/api/ict/pauseDraftProcess/{id}",
    ];

    public static IEndpointRouteBuilder MapIctLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        foreach (var path in AbortPaths)
        {
            app.MapPut(path, AbortAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        foreach (var path in PausePaths)
        {
            app.MapPut(path, PauseAsync).RequireAuthorization(IctSecurityExtensions.IctClientPolicy);
        }

        return app;
    }

    private static async Task<IResult> PauseAsync(
        string id,
        PauseProcessRequest? body,
        PauseDraftHandler handler,
        CancellationToken ct)
    {
        var paused = body?.PauseProcess ?? false;
        var (ok, error) = await handler.HandleAsync(id, paused, body?.Observation, ct);
        if (ok)
        {
            return Results.Ok(new LifecycleV1Result(
                true, paused ? "Trámite pausado exitosamente" : "Trámite reanudado exitosamente", null));
        }

        return error switch
        {
            "not_found" or "not_materialized" =>
                Fail("El trámite no existe o aún no está en borrador", StatusCodes.Status404NotFound),
            "not_in_draft" =>
                Fail("El trámite no se encuentra en estado de borrador", StatusCodes.Status403Forbidden),
            "grpc_unavailable" =>
                Fail("El servicio de trámites no está disponible en este momento", StatusCodes.Status503ServiceUnavailable),
            "unauthenticated" =>
                Fail("No autorizado", StatusCodes.Status401Unauthorized),
            _ => Fail("No se pudo pausar el trámite", StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<IResult> AbortAsync(
        string id,
        AbortProcessRequest? body,
        AbortDraftHandler handler,
        CancellationToken ct)
    {
        var (ok, error) = await handler.HandleAsync(id, body?.Observation, ct);
        if (ok)
        {
            return Results.Ok(new LifecycleV1Result(true, "Trámite anulado exitosamente", null));
        }

        return error switch
        {
            "not_found" or "not_materialized" =>
                Fail("El trámite no existe o aún no está en borrador", StatusCodes.Status404NotFound),
            "transicion_no_permitida" or "estado_final" or "estado_desconocido" =>
                Fail("El trámite no se encuentra en un estado anulable (borrador o rechazado)", StatusCodes.Status403Forbidden),
            "conflicto_concurrencia" =>
                Fail("Conflicto de concurrencia al anular el trámite", StatusCodes.Status409Conflict),
            "grpc_unavailable" =>
                Fail("El servicio de trámites no está disponible en este momento", StatusCodes.Status503ServiceUnavailable),
            "unauthenticated" =>
                Fail("No autorizado", StatusCodes.Status401Unauthorized),
            _ => Fail("No se pudo anular el trámite", StatusCodes.Status400BadRequest),
        };
    }

    private static IResult Fail(string message, int httpStatus) =>
        Results.Json(new LifecycleV1Result(false, message, null), statusCode: httpStatus);
}
