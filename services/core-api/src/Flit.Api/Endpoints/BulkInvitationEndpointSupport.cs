using System.Globalization;
using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Api.RateLimiting;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Cuerpo de error común de la invitación masiva. Lleva <c>code</c> (convención de <c>/security</c>) y <c>error</c>
/// (convención de <c>/admin/ot</c>) con el MISMO valor, para que un cliente único sirva a las dos familias de rutas.
/// </summary>
public sealed record BulkInvitationErrorBody(string Code, string Error, string Message);

/// <summary>
/// Pieza compartida de <c>/api/v1/security/invitations/bulk*</c> (HU #14424) y <c>/api/v1/admin/ot/users/invite/bulk*</c>
/// (HU #14425): arma el contexto de alcance desde el JWT, traduce los resultados a HTTP y deja el detalle de auditoría.
/// Las dos rutas solo difieren en cómo resuelven el DESTINO; todo lo demás es idéntico por diseño (ADR-0077).
/// </summary>
internal static class BulkInvitationEndpointSupport
{
    private const string DryRunQueryKey = "dryRun";

    /// <summary>
    /// ¿La petición es un commit? SOLO con <c>dryRun=false</c> explícito. Ausente o ilegible = previsualización: el valor por
    /// defecto es el seguro (no crea nada).
    /// </summary>
    public static bool IsCommit(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Query.TryGetValue(DryRunQueryKey, out var raw)
            && bool.TryParse(raw.ToString(), out var dryRun)
            && !dryRun;
    }

    public static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    public static List<string> RoleCodes(ClaimsPrincipal user) => [.. RequestTenantResolver.RoleValues(user)];

    public static bool IsSuperAdmin(ClaimsPrincipal user) =>
        RequestTenantResolver.RoleValues(user).Any(r =>
            string.Equals(r, AdminAuthorization.SuperAdminRole, StringComparison.OrdinalIgnoreCase));

    public static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new BulkInvitationErrorBody(code, code, message), statusCode: statusCode);

    /// <summary>
    /// Contexto para <c>/security/invitations/bulk*</c>. SuperAdmin: el destino (<c>targetTenantId</c>) es obligatorio.
    /// AdminCompany: SIEMPRE su propio tenant; un <c>targetTenantId</c> ajeno es 403 y no se procesa nada (HU #14424 AC3).
    /// </summary>
    public static (InvitationScopeContext? Context, IResult? Failure) ResolveSecurityScope(
        ClaimsPrincipal caller, Guid? targetTenantId)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!RequestTenantResolver.TryResolveTenantId(caller, out var callerTenantId) || ResolveUserId(caller) is not { } userId)
        {
            return (null, Results.Unauthorized());
        }

        Guid target;
        if (IsSuperAdmin(caller))
        {
            if (targetTenantId is null || targetTenantId == Guid.Empty)
            {
                return (null, Error(
                    StatusCodes.Status400BadRequest,
                    "TARGET_TENANT_REQUIRED",
                    "El SuperAdmin debe especificar la empresa destino (targetTenantId)."));
            }

            target = targetTenantId.Value;
        }
        else
        {
            if (targetTenantId is { } requested && requested != Guid.Empty && requested != callerTenantId)
            {
                return (null, DeniedResult(InvitationScopeReasons.TargetTenantOutOfScope));
            }

            target = callerTenantId;
        }

        return (new InvitationScopeContext(userId, callerTenantId, RoleCodes(caller), target, InvitationChannel.Security), null);
    }

    public static async Task<IResult> TemplateAsync(
        InvitationScopeContext scope,
        IBulkInvitationXlsxTemplate template,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await template.BuildAsync(scope, cancellationToken).ConfigureAwait(false);
            return Results.File(file.Content, file.Mimetype, file.Filename);
        }
        catch (InvitationScopeDeniedException ex)
        {
            return DeniedResult(ex.Reason);
        }
    }

    public static async Task<IResult> SubmitAsync(
        HttpContext http,
        IFormFile? archivo,
        InvitationScopeContext scope,
        string profile,
        BulkInvitationService service,
        BulkInvitationOptions options,
        BulkInvitationDryRunLimiter dryRunLimiter,
        CancellationToken cancellationToken)
    {
        var commit = IsCommit(http.Request);

        // Frena la enumeración de correos vía dryRun (HU #14424 AC4). El commit no cuenta: ya es una acción real y
        // además se audita.
        if (!commit && !dryRunLimiter.TryAcquire(scope.InviterUserId))
        {
            http.Response.Headers.RetryAfter = BulkInvitationDryRunLimiter.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Error(
                StatusCodes.Status429TooManyRequests,
                "TOO_MANY_REQUESTS",
                "Demasiadas validaciones seguidas. Espera un momento antes de volver a validar el archivo.");
        }

        if (archivo is null || archivo.Length == 0)
        {
            return FileRejected(http, commit, "FILE_REQUIRED", "Debes adjuntar el archivo Excel diligenciado.");
        }

        // Corte barato antes de leer el cuerpo: el parser repite el tope sobre los bytes realmente recibidos.
        if (archivo.Length > options.MaxFileBytes)
        {
            return FileRejected(http, commit, BulkInvitationFileErrors.FileTooLarge, FileErrorMessage(BulkInvitationFileErrors.FileTooLarge, options));
        }

        BulkInvitationResult result;
        try
        {
            await using var content = archivo.OpenReadStream();
            result = await service.ProcessAsync(scope, content, dryRun: !commit, cancellationToken).ConfigureAwait(false);
        }
        catch (InvitationScopeDeniedException ex)
        {
            if (commit)
            {
                ConfigAuditFailureContext.SetErrorCode(http, ex.Reason.ToLowerInvariant());
            }

            return DeniedResult(ex.Reason);
        }

        if (result.FileError is { } fileError)
        {
            return FileRejected(http, commit, fileError, FileErrorMessage(fileError, options));
        }

        var report = result.Report!;

        if (commit)
        {
            // UNA fila resumen por carga (HU #14424 AC2): quién carga (actor de la fila), destino y conteos. Sin correos
            // ni nombres: el reporte por fila vive solo en la respuesta (ADR-0077: el lote no se persiste).
            AdminAuditDetailContext.SetChange(
                http,
                oldValue: null,
                newValue: new
                {
                    profile,
                    targetTenantId = scope.TargetTenantId,
                    total = report.Total,
                    valid = report.Valid,
                    created = report.Created,
                    rejected = report.Rejected,
                    notProcessed = report.NotProcessed,
                });
        }

        return Results.Ok(report);
    }

    /// <summary>403 (404 si el destino no existe) con el motivo estable de la política de alcance.</summary>
    public static IResult DeniedResult(string reason) =>
        reason == InvitationScopeReasons.TargetTenantNotFound
            ? Error(StatusCodes.Status404NotFound, reason, "No existe el destino indicado.")
            : Error(StatusCodes.Status403Forbidden, reason, ScopeMessage(reason));

    private static IResult FileRejected(HttpContext http, bool commit, string code, string message)
    {
        if (commit)
        {
            ConfigAuditFailureContext.SetErrorCode(http, code.ToLowerInvariant());
        }

        return Error(
            code == BulkInvitationFileErrors.FileTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
            code,
            message);
    }

    private static string FileErrorMessage(string code, BulkInvitationOptions options) => code switch
    {
        BulkInvitationFileErrors.FileTooLarge =>
            $"El archivo supera el tamaño máximo de {(options.MaxFileBytes / 1024).ToString(CultureInfo.InvariantCulture)} KB.",
        BulkInvitationFileErrors.TooManyRows =>
            $"El archivo supera el máximo de {options.MaxRows.ToString(CultureInfo.InvariantCulture)} filas. Pártelo e inténtalo de nuevo.",
        BulkInvitationFileErrors.TemplateInvalid =>
            "El archivo no usa la plantilla: faltan o cambiaron las columnas Nombre completo, Correo y Rol. Descarga la plantilla de nuevo.",
        BulkInvitationFileErrors.EmptyFile => "El archivo no trae ninguna fila de datos.",
        _ => "El archivo no es un .xlsx válido.",
    };

    private static string ScopeMessage(string reason) => reason switch
    {
        InvitationScopeReasons.TargetTenantOutOfScope => "No tienes alcance sobre el destino indicado.",
        InvitationScopeReasons.CannotInviteToOwnTenant =>
            "El SuperAdmin no puede invitar usuarios a su propio tenant desde la carga masiva (el perfil FLIT no está disponible).",
        InvitationScopeReasons.TargetNotTransitOffice => "El destino no es un organismo de tránsito.",
        _ => "No tienes permiso para invitar usuarios de forma masiva en este destino.",
    };
}
