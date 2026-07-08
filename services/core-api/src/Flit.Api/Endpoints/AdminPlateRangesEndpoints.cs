using System.Security.Claims;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// Consola OT de preasignación de placa (HU #10651, Feature #10587): el OT asigna/edita rangos a
/// las compañías con la preasignación activa (grant vigente + allow_plate_preassign), con ventana de
/// edición de 60 min, y bloquea/revoca placas. Autorización en <c>IsAssignmentAllowedAsync</c>.
/// </summary>
public static class AdminPlateRangesEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlateRangesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plate-ranges")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Preasignación de placa");

        group.MapGet("/", ListRangesAsync).WithName("AdminPlateListRanges");
        group.MapGet("/plates", ListPlatesAsync).WithName("AdminPlateListPlates");
        group.MapPost("/", AssignRangeAsync).WithName("AdminPlateAssignRange");
        group.MapPut("/{rangeId:guid}", EditRangeAsync).WithName("AdminPlateEditRange");
        group.MapPost("/plates/{plateId:guid}/block", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Bloqueada, repo, ct)).WithName("AdminPlateBlock");
        group.MapPost("/plates/{plateId:guid}/unblock", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Disponible, repo, ct)).WithName("AdminPlateUnblock");
        group.MapPost("/plates/{plateId:guid}/revoke", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Revocada, repo, ct)).WithName("AdminPlateRevoke");

        return app;
    }

    private static async Task<IResult> ListRangesAsync(
        Guid companyTenantId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
    {
        if (!TryResolveOfficeAsync(http, repo, ct, out var officeTask, out var err))
        {
            return err!;
        }

        var officeId = await officeTask!.ConfigureAwait(false);
        var ranges = await repo.ListRangesAsync(companyTenantId, officeId, ct).ConfigureAwait(false);
        return Results.Ok(ranges);
    }

    private static async Task<IResult> ListPlatesAsync(
        Guid companyTenantId, string? state, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
    {
        if (state is not null && !PlateState.EsValido(state))
        {
            return Results.BadRequest(new { error = "Estado de placa inválido." });
        }

        if (!TryResolveOfficeAsync(http, repo, ct, out var officeTask, out var err))
        {
            return err!;
        }

        var officeId = await officeTask!.ConfigureAwait(false);
        var plates = await repo.ListDetailsAsync(companyTenantId, officeId, state, ct).ConfigureAwait(false);
        return Results.Ok(plates);
    }

    private static async Task<IResult> AssignRangeAsync(
        AssignPlateRangeRequest request, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
    {
        var officeId = await ResolveOfficeIdAsync(http, request.TransitOfficeId, repo, ct).ConfigureAwait(false);
        if (officeId is null)
        {
            return Results.BadRequest(new { error = "No se pudo resolver el organismo de tránsito." });
        }

        if (!await repo.IsAssignmentAllowedAsync(request.CompanyTenantId, officeId.Value, ct).ConfigureAwait(false))
        {
            return Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "La preasignación no está habilitada entre la compañía y el OT (flag, grant o allow_plate_preassign).");
        }

        var result = await repo.CreateRangeAsync(
            request.CompanyTenantId, officeId.Value, request.Prefix, request.RangeFrom, request.RangeTo,
            ResolveUserId(http.User), ct).ConfigureAwait(false);

        return result.Success
            ? Results.Ok(new { rangeId = result.RangeId, platesCreated = result.PlatesCreated })
            : Results.Problem(statusCode: 422, title: "Unprocessable", detail: result.Error);
    }

    private static async Task<IResult> EditRangeAsync(
        Guid rangeId, EditPlateRangeRequest request, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
    {
        var result = await repo.EditRangeAsync(
            rangeId, request.Prefix, request.RangeFrom, request.RangeTo, ResolveUserId(http.User), ct)
            .ConfigureAwait(false);

        return result.Success
            ? Results.Ok(new { rangeId = result.RangeId, platesCreated = result.PlatesCreated })
            : Results.Problem(statusCode: 422, title: "Unprocessable", detail: result.Error);
    }

    private static async Task<IResult> SetStateAsync(
        Guid plateId, string targetState, IPlateRangeRepository repo, CancellationToken ct)
    {
        var result = await repo.SetPlateStateAsync(plateId, targetState, ct).ConfigureAwait(false);
        return result.Success
            ? Results.Ok()
            : Results.Problem(statusCode: 422, title: "Unprocessable", detail: result.Error);
    }

    private static bool TryResolveOfficeAsync(
        HttpContext http, IPlateRangeRepository repo, CancellationToken ct,
        out Task<Guid?>? officeTask, out IResult? error)
    {
        officeTask = null;
        error = null;

        var superAdminOffice = http.Request.Query.TryGetValue("transitOfficeId", out var raw)
            && Guid.TryParse(raw, out var oid) ? (Guid?)oid : null;

        officeTask = ResolveOfficeIdAsync(http, superAdminOffice, repo, ct);
        return true;
    }

    private static async Task<Guid?> ResolveOfficeIdAsync(
        HttpContext http, Guid? explicitOfficeId, IPlateRangeRepository repo, CancellationToken ct)
    {
        var isSuperAdmin = http.User.IsInRole(AdminAuthorization.SuperAdminRole);
        if (isSuperAdmin && explicitOfficeId is { } explicitId && explicitId != Guid.Empty)
        {
            return explicitId;
        }

        var tenantClaim = http.User.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(tenantClaim, out var otTenantId)
            ? await repo.ResolveOfficeIdAsync(otTenantId, ct).ConfigureAwait(false)
            : null;
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

/// <summary>Payload para asignar un rango a una compañía (consola OT).</summary>
public sealed record AssignPlateRangeRequest(
    Guid CompanyTenantId,
    string Prefix,
    int RangeFrom,
    int RangeTo,
    Guid? TransitOfficeId = null);

/// <summary>Payload para editar un rango (dentro de la ventana de 60 min).</summary>
public sealed record EditPlateRangeRequest(string Prefix, int RangeFrom, int RangeTo);
