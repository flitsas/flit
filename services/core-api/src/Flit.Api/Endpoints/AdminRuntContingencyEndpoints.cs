using System.Security.Claims;
using Flit.Admin.Application.Plataforma.RuntContingency;
using Flit.Api.Authorization;
using Flit.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>
/// Administración → Plataforma → Contingencia de proveedor RUNT (Épica #14644, Feature #14645, HU #14648). Solo el Super Admin FLIT:
/// el permiso <c>runt_contingency.manage</c> se siembra únicamente para él. Por tipo de consulta (<c>vehicle_vin</c>,
/// <c>vehicle_plate</c>, <c>conductor</c>) fuerza un proveedor RUNT para Trámites, Persona Natural e ICT sin tocar la
/// configuración de ninguna empresa. La contingencia vive y se aplica en core-consultas; aquí solo se consulta y se cambia, y cada
/// cambio queda en la auditoría de Plataforma (nunca en el historial de una empresa).
/// </summary>
public static class AdminRuntContingencyEndpoints
{
    public const string ManagePermission = "runt_contingency.manage";
    public const string UnavailableDetail = "El servicio de consultas no responde. Intenta de nuevo en unos minutos.";

    public static IEndpointRouteBuilder MapAdminRuntContingencyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/platform/runt-contingency")
            .RequirePermission(ManagePermission)
            .WithTags("Admin · Plataforma · Contingencia RUNT");

        group.MapGet("", ListAsync)
            .WithName("AdminRuntContingencyList")
            .WithSummary("Estado de la contingencia de proveedor RUNT por tipo de consulta")
            .Produces<RuntContingencyListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{kind}", SaveAsync)
            .WithName("AdminRuntContingencySave")
            .WithSummary("Activa, cambia o apaga (provider = null) la contingencia de un tipo; el motivo es obligatorio")
            .Produces<RuntContingencyItemResponse>(StatusCodes.Status200OK)
            .Produces<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] GetRuntContingencyHandler handler,
        CancellationToken ct)
    {
        try
        {
            var items = await handler.HandleAsync(ct).ConfigureAwait(false);
            return Results.Ok(new RuntContingencyListResponse([.. items.Select(RuntContingencyItemResponse.From)]));
        }
        catch (RuntContingencyUnavailableException)
        {
            return Unavailable();
        }
    }

    private static async Task<IResult> SaveAsync(
        string kind,
        [FromBody] SaveRuntContingencyRequest? request,
        ClaimsPrincipal user,
        [FromServices] SaveRuntContingencyHandler handler,
        [FromServices] FlitDbContext db,
        CancellationToken ct)
    {
        // Tipo desconocido: 404 antes de mirar el cuerpo.
        if (!RuntContingencyCatalog.IsKind(kind))
            return Results.NotFound();

        var actorId = AdminRuntConfirmationEndpoints.ResolveUserId(user);
        try
        {
            var actorName = await ActorNameAsync(user, actorId, db, ct).ConfigureAwait(false);
            var result = await handler.HandleAsync(
                new SaveRuntContingencyCommand(kind, request?.Provider, request?.Reason, actorId, actorName), ct).ConfigureAwait(false);

            return result.Outcome switch
            {
                SaveRuntContingencyOutcome.KindNotFound => Results.NotFound(),
                SaveRuntContingencyOutcome.Invalid => Results.ValidationProblem(result.Errors.ToDictionary(e => e.Key, e => e.Value)),
                _ => Results.Ok(RuntContingencyItemResponse.From(result.Item!)),
            };
        }
        catch (RuntContingencyUnavailableException)
        {
            return Unavailable();
        }
    }

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Servicio de consultas no disponible", detail: UnavailableDetail);

    /// <summary>Nombre visible de quien cambia: el del usuario en la base; si no se encuentra, el correo del token.</summary>
    private static async Task<string?> ActorNameAsync(ClaimsPrincipal user, Guid? actorId, FlitDbContext db, CancellationToken ct)
    {
        if (actorId is { } id)
        {
            string? nombre = null;
            try
            {
                nombre = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El nombre es solo para mostrar: si no se puede leer, el cambio sigue con el correo del token.
            }

            if (!string.IsNullOrWhiteSpace(nombre))
                return nombre;
        }

        return user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }
}

/// <summary>Cuerpo del PUT: <c>provider = null</c> apaga la contingencia; <c>reason</c> es obligatorio (1 a 500 caracteres recortado).</summary>
public sealed record SaveRuntContingencyRequest(string? Provider, string? Reason);

/// <summary>Estado de un tipo de consulta: <c>provider = null</c> es «Sin contingencia».</summary>
public sealed record RuntContingencyItemResponse(string Kind, string? Provider, string? Reason, string? UpdatedByName, DateTimeOffset? UpdatedAt)
{
    internal static RuntContingencyItemResponse From(RuntContingencyItem item) =>
        new(item.Kind, item.Provider, item.Reason, item.UpdatedByName, item.UpdatedAt);
}

/// <summary>Los tres tipos, siempre, en el orden vehicle_vin, vehicle_plate, conductor.</summary>
public sealed record RuntContingencyListResponse(IReadOnlyList<RuntContingencyItemResponse> Items);
