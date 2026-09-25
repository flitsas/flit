using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.DrFlit.Application.Chat;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// DR. FLIT — chat con LLM sobre el manual (HU #12922, Épica #12718, ADR-0060 §5.1).
/// <para>
/// Cualquier usuario autenticado (sin policy). El tope diario se cuenta por tenant y usuario, así que el
/// tenant NO sale del header para un usuario de compañía: sale del token, igual que en
/// <see cref="Middleware.TenantEnforcementMiddleware"/>. Si no, cambiar <c>X-Tenant-Id</c> daría un cupo
/// nuevo. El SuperAdmin, que no tiene compañía propia, usa la del header.
/// </para>
/// <para>
/// LLM caído, fuera de contrato o tope alcanzado NO son errores HTTP: responden 200 con
/// <c>status</c> = <c>degraded</c> o <c>rate_limited</c> (§5.1). Solo la entrada inválida es 400, y en
/// ese caso no se consume tope ni se llama al modelo.
/// </para>
/// </summary>
public static class DrFlitEndpoints
{
    public const int MaxMessageLength = 2000;
    public const int MaxHistoryTurns = 12;

    public static IEndpointRouteBuilder MapDrFlitEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/dr-flit").RequireAuthorization();

        group.MapPost("/chat", ChatAsync)
            .WithName("DrFlitChat")
            .WithSummary("Clasifica la intención del mensaje libre y responde dudas citando el manual")
            .Produces<DrFlitChatResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    internal static async Task<IResult> ChatAsync(
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantHeader,
        [FromBody] DrFlitChatRequestBody? body,
        IDrFlitAssistant assistant,
        CancellationToken cancellationToken)
    {
        if (tenantHeader is null || tenantHeader == Guid.Empty)
            return BadRequest("Falta header X-Tenant-Id");

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
            return Results.Unauthorized();

        Guid tenantId;
        if (RequestTenantResolver.IsSuperAdmin(httpContext.User))
        {
            tenantId = tenantHeader.Value;
        }
        else if (!RequestTenantResolver.TryResolveNonEmptyTenantId(httpContext.User, out tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "El usuario autenticado no tiene una compañía asignada.");
        }

        var validation = Validate(body);
        if (validation is not null)
            return BadRequest(validation);

        // Validate ya garantizó que no hay turnos null, con role conocido y con texto.
        var history = (body!.History ?? [])
            .OfType<DrFlitChatTurnBody>()
            .Select(t => new DrFlitTurn(
                string.Equals(t.Role, "user", StringComparison.Ordinal) ? DrFlitTurnRole.User : DrFlitTurnRole.Assistant,
                t.Text!))
            .ToList();

        var result = await assistant
            .AskAsync(new DrFlitChatRequest(tenantId, userId.Value, body.Message!.Trim(), history), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ToResponse(result));
    }

    /// <summary>Validación de entrada (AC3). Devuelve el motivo o <c>null</c> si es válida.</summary>
    internal static string? Validate(DrFlitChatRequestBody? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Message))
            return "El mensaje no puede estar vacío.";
        if (body.Message.Length > MaxMessageLength)
            return $"El mensaje supera {MaxMessageLength} caracteres.";

        var history = body.History ?? [];
        if (history.Count > MaxHistoryTurns)
            return $"El historial admite máximo {MaxHistoryTurns} turnos.";

        foreach (var turn in history)
        {
            if (turn is null || (turn.Role is not ("user" or "assistant")))
                return "Cada turno del historial debe tener role 'user' o 'assistant'.";
            if (string.IsNullOrWhiteSpace(turn.Text))
                return "Cada turno del historial debe tener texto.";
            if (turn.Text.Length > MaxMessageLength)
                return $"Un turno del historial supera {MaxMessageLength} caracteres.";
        }

        return null;
    }

    internal static DrFlitChatResponse ToResponse(DrFlitChatResult result) => new(
        Status: result.Status.ToWire(),
        // Sin respuesta válida del LLM no hay intención: el contrato pide un valor y no_claro es el que
        // no dispara ninguna acción en el frontend.
        Intent: (result.Intent ?? DrFlitIntent.NoClaro).ToWire(),
        Reply: result.Reply,
        Citations: [.. result.Citations.Select(a => new DrFlitCitationResponse(
            a.Slug, a.Title, a.Href, a.SourceHref, a.PrimarySource))],
        SuggestGestionIntent: null,
        Usage: new DrFlitUsageResponse(result.MessagesUsedToday, result.DailyLimit));

    private static IResult BadRequest(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);

    /// <summary>Id del usuario autenticado (claim <c>sub</c>/NameIdentifier), o null si no resuelve.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Body de <c>POST /api/v1/dr-flit/chat</c>.</summary>
public sealed class DrFlitChatRequestBody
{
    public string? Message { get; set; }

    /// <summary>Últimos turnos de la conversación (los guarda el cliente en sessionStorage).</summary>
    public IReadOnlyList<DrFlitChatTurnBody?>? History { get; set; }

    /// <summary>Módulo o ruta actual. Se acepta por contrato; hoy el chat no lo usa.</summary>
    public string? RouteScope { get; set; }
}

public sealed class DrFlitChatTurnBody
{
    public string? Role { get; set; }

    public string? Text { get; set; }
}

public sealed record DrFlitChatResponse(
    string Status,
    string Intent,
    string Reply,
    IReadOnlyList<DrFlitCitationResponse> Citations,
    string? SuggestGestionIntent,
    DrFlitUsageResponse Usage);

public sealed record DrFlitCitationResponse(
    string Slug,
    string Title,
    string Href,
    string? SourceHref,
    bool PrimarySource);

public sealed record DrFlitUsageResponse(int MessagesUsedToday, int DailyLimit);
