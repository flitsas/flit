using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using Flit.DrFlit.Application.Manual;
using Flit.DrFlit.Application.SupportCases;
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

        // HU #12931 — consentimiento de tratamiento de datos: consultar y aceptar la versión vigente.
        group.MapGet("/consent", GetConsentAsync)
            .WithName("DrFlitGetConsent")
            .WithSummary("Versión vigente del tratamiento de datos de DR. FLIT y si el usuario ya la aceptó")
            .Produces<DrFlitConsentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/consent", AcceptConsentAsync)
            .WithName("DrFlitAcceptConsent")
            .WithSummary("Registra que el usuario aceptó la versión vigente del tratamiento de datos")
            .Produces<DrFlitConsentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/chat", ChatAsync)
            .AddEndpointFilter<DrFlitConsentRequiredFilter>()
            .WithName("DrFlitChat")
            .WithSummary("Clasifica la intención del mensaje libre y responde dudas citando el manual")
            .Produces<DrFlitChatResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/support-cases", CreateSupportCaseAsync)
            .AddEndpointFilter<DrFlitConsentRequiredFilter>()
            .WithName("DrFlitCreateSupportCase")
            .WithSummary("Crea un caso de soporte (Bug en FLIT - SOPORTE) a partir del formulario confirmado")
            .Produces<DrFlitSupportCaseCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/support-cases/attachments", UploadAttachmentAsync)
            .AddEndpointFilter<DrFlitConsentRequiredFilter>()
            .WithName("DrFlitUploadSupportAttachment")
            .WithSummary("Sube un adjunto temporal para un caso de soporte aún no creado")
            .DisableAntiforgery() // API con JWT en header, sin cookies: el antiforgery no aplica (igual que banners/branding)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<DrFlitSupportAttachmentResponse>(StatusCodes.Status201Created)
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
        var caller = ResolveCaller(httpContext, tenantHeader);
        if (caller.Error is not null)
            return caller.Error;
        var (tenantId, userId) = (caller.TenantId, caller.UserId);

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
            .AskAsync(
                new DrFlitChatRequest(
                    tenantId, userId, body.Message!.Trim(), history, ResolveManualProfile(httpContext.User)),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ToResponse(result));
    }

    /// <summary>
    /// HU #12925 — radica el caso confirmado por el usuario. 201 con el número de caso (el enlace al work
    /// item solo para SuperAdmin); 400 si el formulario o los adjuntos no son válidos; 502 si el sistema de
    /// soporte no respondió tras el reintento, con <c>code = support_unavailable</c> para que el frontend
    /// ofrezca los canales de soporte estáticos.
    /// </summary>
    internal static async Task<IResult> CreateSupportCaseAsync(
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantHeader,
        [FromBody] DrFlitSupportCaseRequestBody? body,
        CreateSupportCaseHandler handler,
        CancellationToken cancellationToken)
    {
        var caller = ResolveCaller(httpContext, tenantHeader);
        if (caller.Error is not null)
            return caller.Error;
        if (body is null)
            return BadRequest("Falta el formulario del caso.");

        var result = await handler.HandleAsync(
            new CreateSupportCaseCommand(
                caller.TenantId, caller.UserId, RequestTenantResolver.IsSuperAdmin(httpContext.User),
                body.Nombre, body.Email, body.Telefono, body.Compania, body.Detalle, body.ResultadoEsperado,
                body.Frecuencia, body.Titulo, body.Prioridad, body.AffectedModule, body.AttachmentIds),
            cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            CreateSupportCaseOutcome.Created => Results.Created(
                (string?)null, new DrFlitSupportCaseCreatedResponse(result.CaseId!.Value, result.CaseUrl, result.AttachmentsFailed)),
            CreateSupportCaseOutcome.ProviderUnavailable => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Soporte no disponible",
                detail: result.Error,
                extensions: new Dictionary<string, object?> { ["code"] = "support_unavailable" }),
            _ => BadRequest(result.Error ?? "Formulario inválido."),
        };
    }

    /// <summary>
    /// HU #12924 — adjunto de un caso aún no confirmado. 201 con el id que el formulario manda luego en
    /// <c>attachmentIds</c>; 400 si falta, no es de un tipo permitido o supera el tamaño configurado.
    /// </summary>
    internal static async Task<IResult> UploadAttachmentAsync(
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantHeader,
        IFormFile? file,
        UploadSupportAttachmentHandler handler,
        CancellationToken cancellationToken)
    {
        var caller = ResolveCaller(httpContext, tenantHeader);
        if (caller.Error is not null)
            return caller.Error;

        await using var content = file?.OpenReadStream();
        var result = await handler.HandleAsync(
            new UploadSupportAttachmentCommand(
                caller.TenantId, caller.UserId, file?.FileName, file?.ContentType, file?.Length ?? 0, content),
            cancellationToken).ConfigureAwait(false);

        return result.Outcome == UploadSupportAttachmentOutcome.Uploaded
            ? Results.Created(
                (string?)null,
                new DrFlitSupportAttachmentResponse(result.Attachment!.Id, result.Attachment.FileName, result.Attachment.SizeBytes))
            : BadRequest(result.Error ?? "Adjunto inválido.");
    }

    /// <summary>HU #12931 — versión vigente y si el usuario ya la aceptó (una vez por usuario y versión).</summary>
    internal static async Task<IResult> GetConsentAsync(
        HttpContext httpContext,
        IDrFlitConsentStore store,
        IDrFlitConsentSettings settings,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
            return Results.Unauthorized();

        var version = settings.CurrentVersion;
        var accepted = await store.HasAcceptedAsync(userId.Value, version, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new DrFlitConsentResponse(version, accepted));
    }

    /// <summary>
    /// HU #12931 — registra la aceptación con su evidencia (fecha, IP, user agent). Solo se acepta la
    /// versión vigente: si el cliente muestra un texto viejo, 409 con la versión que corresponde.
    /// </summary>
    internal static async Task<IResult> AcceptConsentAsync(
        HttpContext httpContext,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantHeader,
        [FromBody] DrFlitConsentAcceptBody? body,
        IDrFlitConsentStore store,
        IDrFlitConsentSettings settings,
        IAuditContextAccessor auditContext,
        CancellationToken cancellationToken)
    {
        var caller = ResolveCaller(httpContext, tenantHeader);
        if (caller.Error is not null)
            return caller.Error;
        if (string.IsNullOrWhiteSpace(body?.Version))
            return BadRequest("Falta la versión aceptada.");

        var current = settings.CurrentVersion;
        if (!string.Equals(body.Version.Trim(), current, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Versión desactualizada",
                detail: "El texto de tratamiento de datos cambió. Léelo de nuevo antes de aceptarlo.",
                extensions: new Dictionary<string, object?> { ["code"] = "consent_version_mismatch", ["version"] = current });
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        await store.RecordAsync(
            new DrFlitConsentAcceptance(
                caller.TenantId, caller.UserId, current, auditContext.ClientIp, userAgent.Length > 0 ? userAgent : null),
            cancellationToken).ConfigureAwait(false);

        return Results.Created((string?)null, new DrFlitConsentResponse(current, true));
    }

    /// <summary>
    /// HU #13023 — perfil EFECTIVO del rol del JWT, con la MISMA precedencia que
    /// <c>roleFromPayload</c>/<c>isOtUser</c> del frontend (<c>dr-flit-context.ts</c>): SuperAdmin →
    /// OT (rol <c>ot_admin</c> o tenant de tipo TRANSIT_OFFICE) → AdminCompany → Gestor. Acota el
    /// manual del <c>system</c> a la audiencia de quien pregunta.
    /// </summary>
    internal static DrFlitManualProfile ResolveManualProfile(ClaimsPrincipal user)
    {
        if (RequestTenantResolver.IsSuperAdmin(user))
            return DrFlitManualProfile.SuperAdmin;
        if (RequestTenantResolver.HasRole(user, AdminAuthorization.OtAdminRole) || IsTransitOfficeTenant(user))
            return DrFlitManualProfile.OtAdmin;
        if (RequestTenantResolver.HasRole(user, AdminAuthorization.AdminCompanyRole))
            return DrFlitManualProfile.AdminCompany;
        return DrFlitManualProfile.Gestor;
    }

    private static bool IsTransitOfficeTenant(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(AdminAuthorization.EntityTypeClaimType),
            AdminAuthorization.TransitOfficeEntityType,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tenant y usuario de la petición. El tenant del cupo y de los adjuntos sale del token para un usuario
    /// de compañía (el header se exige pero no manda); el SuperAdmin usa el del header.
    /// </summary>
    private static (Guid TenantId, Guid UserId, IResult? Error) ResolveCaller(HttpContext httpContext, Guid? tenantHeader)
    {
        if (tenantHeader is null || tenantHeader == Guid.Empty)
            return (default, default, BadRequest("Falta header X-Tenant-Id"));

        var userId = ResolveUserId(httpContext.User);
        if (userId is null)
            return (default, default, Results.Unauthorized());

        if (RequestTenantResolver.IsSuperAdmin(httpContext.User))
            return (tenantHeader.Value, userId.Value, null);

        return RequestTenantResolver.TryResolveNonEmptyTenantId(httpContext.User, out var tenantId)
            ? (tenantId, userId.Value, null)
            : (default, default, Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "El usuario autenticado no tiene una compañía asignada."));
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
        SuggestGestionIntent: result.GestionTarget,
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

/// <summary>Body de <c>POST /api/v1/dr-flit/support-cases</c> (nombres del contrato §5.2, en español).</summary>
public sealed class DrFlitSupportCaseRequestBody
{
    public string? Nombre { get; set; }

    public string? Email { get; set; }

    public string? Telefono { get; set; }

    public string? Compania { get; set; }

    public string? Detalle { get; set; }

    public string? ResultadoEsperado { get; set; }

    /// <summary><c>una_vez</c> | <c>a_veces</c> | <c>siempre</c>.</summary>
    public string? Frecuencia { get; set; }

    public string? Titulo { get; set; }

    /// <summary><c>Alta</c> | <c>Media</c> | <c>Baja</c>.</summary>
    public string? Prioridad { get; set; }

    /// <summary>Mejor esfuerzo del cliente (ruta actual); el backend lo valida contra el allow-list.</summary>
    public string? AffectedModule { get; set; }

    public IReadOnlyList<Guid>? AttachmentIds { get; set; }
}

/// <summary>Respuesta 201 de <c>POST /api/v1/dr-flit/support-cases</c>.</summary>
/// <param name="CaseUrl">Solo si el caller es SuperAdmin; para el resto, <c>null</c>.</param>
public sealed record DrFlitSupportCaseCreatedResponse(int CaseId, string? CaseUrl, int AttachmentsFailed);

/// <summary>HU #12931 — estado del consentimiento del usuario.</summary>
public sealed record DrFlitConsentResponse(string Version, bool Accepted);

/// <summary>Body de <c>POST /api/v1/dr-flit/consent</c>: la versión del texto que el usuario leyó.</summary>
public sealed class DrFlitConsentAcceptBody
{
    public string? Version { get; set; }
}

/// <summary>Respuesta 201 de <c>POST /api/v1/dr-flit/support-cases/attachments</c>.</summary>
public sealed record DrFlitSupportAttachmentResponse(Guid Id, string Filename, long SizeBytes);
