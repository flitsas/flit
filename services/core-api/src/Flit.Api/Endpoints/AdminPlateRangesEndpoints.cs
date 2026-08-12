using System.Security.Claims;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Api.Authorization;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        // HU #10797 — compañías elegibles del OT para el selector de asignación de rango.
        group.MapGet("/eligible-companies", EligibleCompaniesAsync).WithName("AdminPlateEligibleCompanies");
        group.MapPost("/", AssignRangeAsync).WithName("AdminPlateAssignRange");
        group.MapPut("/{rangeId:guid}", EditRangeAsync).WithName("AdminPlateEditRange");
        group.MapPost("/plates/{plateId:guid}/block", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Bloqueada, repo, ct)).WithName("AdminPlateBlock");
        group.MapPost("/plates/{plateId:guid}/unblock", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Disponible, repo, ct)).WithName("AdminPlateUnblock");
        group.MapPost("/plates/{plateId:guid}/revoke", (Guid plateId, HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
            => SetStateAsync(plateId, PlateState.Revocada, repo, ct)).WithName("AdminPlateRevoke");

        // HU #10654 — el OT asigna una placa a un trámite en preasignado (Flujo B).
        group.MapPost("/procedures/{instanceId:guid}/assign-plate", AssignPlateToProcedureAsync)
            .WithName("AdminPlateAssignToProcedure");

        // HU #10655 — el OT revoca la preasignación de un trámite.
        group.MapPost("/procedures/{instanceId:guid}/revoke", RevokeProcedurePlateAsync)
            .WithName("AdminPlateRevokeProcedure");

        return app;
    }

    private static async Task<IResult> RevokeProcedurePlateAsync(
        Guid instanceId, RevokePlateRequest request, HttpContext http,
        IOtClientProcedureRepository otRepo, CancellationToken ct)
    {
        var tenantClaim = http.User.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        if (!Guid.TryParse(tenantClaim, out var otTenantId))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "No se pudo resolver el OT.");
        }

        var result = await otRepo.RevokePlateAsync(
            otTenantId, instanceId, request.Reason, ResolveUserId(http.User), "ot_console", ct)
            .ConfigureAwait(false);

        return result is null
            ? Results.Problem(statusCode: 422, title: "Unprocessable",
                detail: "No se pudo revocar: el trámite no es accesible o no está en preasignado/asignado.")
            : Results.Ok(result);
    }

    private static async Task<IResult> AssignPlateToProcedureAsync(
        Guid instanceId, AssignPlateToProcedureRequest request, HttpContext http,
        IOtClientProcedureRepository otRepo, GenerarFurHandler furHandler,
        IPlateAssignmentEmailEnqueuer plateAssignmentEmailEnqueuer,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var tenantClaim = http.User.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        if (!Guid.TryParse(tenantClaim, out var otTenantId))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "No se pudo resolver el OT.");
        }

        if (string.IsNullOrWhiteSpace(request.Plate))
        {
            return Results.Problem(statusCode: 422, title: "Unprocessable", detail: "La placa es obligatoria.");
        }

        // HU #10800 — placa fuera de rango: validar el formato antes de intentar registrarla (AC6).
        if (request.OutOfRange && !PlateRangeRules.IsValidPlate(request.Plate.Trim().ToUpperInvariant()))
        {
            return Results.Problem(statusCode: 422, title: "Unprocessable",
                detail: "La placa debe tener el formato de matrícula (3 letras + 3 dígitos, ej. ABC123).");
        }

        PlateAssignmentOutcome outcome;
        try
        {
            outcome = await otRepo.AssignPlateAsync(
                otTenantId, instanceId, request.Plate, ResolveUserId(http.User), "ot_console", request.OutOfRange, ct)
                .ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Índices únicos y row_version del inventario: el operador del OT necesita saber que debe
            // reintentar o elegir otra placa, no una traza de EF con un 500.
            AdminPlateRegenLog.AsignacionPlacaConflicto(
                loggerFactory.CreateLogger("AdminPlate.AssignPlate"), ex, instanceId);
            return Results.Problem(
                statusCode: 409,
                title: "Conflict",
                detail: $"No se pudo asignar la placa {request.Plate.Trim().ToUpperInvariant()}: el trámite o la placa cambiaron durante la operación. Verifique el estado del trámite e intente de nuevo.");
        }

        if (!outcome.Succeeded)
        {
            // Cada causa con su mensaje: el operador tiene que saber si debe elegir otra placa,
            // registrarla fuera de rango o revisar el estado del trámite. Si el repo aportó un
            // Detail concreto (p.ej. fuera de rango ya registrada), se prioriza sobre el genérico.
            var plate = request.Plate.Trim().ToUpperInvariant();
            var detail = outcome.Detail ?? outcome.Failure switch
            {
                PlateAssignmentFailure.PlateAlreadyAssigned =>
                    $"La placa {plate} ya está asignada a otro trámite de este organismo de tránsito. Elija una placa diferente.",
                PlateAssignmentFailure.PlateInUseByAnotherProcedure =>
                    $"La placa {plate} ya está registrada en otro trámite abierto. Elija una placa diferente.",
                PlateAssignmentFailure.PlateNotAvailable =>
                    $"La placa {plate} no está disponible en los rangos del organismo de tránsito. Elija una del rango o regístrela como fuera de rango.",
                PlateAssignmentFailure.NotPreassigned =>
                    "El trámite no está en preasignado: no admite asignación de placa en su estado actual.",
                PlateAssignmentFailure.ProcedureNotAccessible =>
                    "El trámite no existe o el organismo de tránsito no tiene acceso vigente a él.",
                _ => "La placa es obligatoria.",
            };

            var statusCode = outcome.Failure switch
            {
                PlateAssignmentFailure.PlateAlreadyAssigned => 409,
                PlateAssignmentFailure.PlateNotAvailable => 409,
                PlateAssignmentFailure.PlateInUseByAnotherProcedure => 409,
                PlateAssignmentFailure.ProcedureNotAccessible => 404,
                _ => 422,
            };
            var title = statusCode switch
            {
                409 => "Conflict",
                404 => "Not Found",
                _ => "Unprocessable",
            };
            return Results.Problem(statusCode: statusCode, title: title, detail: detail);
        }

        var result = outcome.Procedure!;

        // HU #10995 — al asignar la placa (Flujo B), regenerar el FUR y los demás documentos en caliente
        // para que reflejen la placa recién escrita en field_values; la regeneración del FUR además invalida
        // los consolidados (se regeneran en la próxima consulta). Best-effort en el scope RLS del cliente:
        // un fallo aquí NO revierte la asignación de placa ya persistida.
        var procedure = await otRepo.GetByIdAsync(otTenantId, instanceId, ct).ConfigureAwait(false);
        if (procedure is not null)
        {
            try
            {
                await otRepo
                    .ExecuteInClientTenantScopeAsync(
                        procedure.ClientTenantId,
                        () => furHandler.HandleAsync(instanceId, procedure.ClientTenantId, ct),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AdminPlateRegenLog.RegeneracionPlacaOmitida(
                    loggerFactory.CreateLogger("AdminPlate.AssignPlateRegen"), ex, instanceId);
            }

            // HU #11485 (Feature #11482, ADR-0046) — aviso de correo al comprador tras asignar placa
            // (Flujo B). Best-effort en el scope RLS del cliente: un fallo NO revierte la asignación.
            try
            {
                var plate = request.Plate.Trim().ToUpperInvariant();
                await otRepo
                    .ExecuteInClientTenantScopeAsync(
                        procedure.ClientTenantId,
                        async () =>
                        {
                            await plateAssignmentEmailEnqueuer
                                .EnqueueAsync(procedure.ClientTenantId, instanceId, plate, ct)
                                .ConfigureAwait(false);
                            return 0;
                        },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AdminPlateRegenLog.CorreoAsignacionPlacaOmitido(
                    loggerFactory.CreateLogger("AdminPlate.AssignPlateEmail"), ex, instanceId);
            }
        }

        return Results.Ok(result);
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

    // HU #10797 — lista las compañías elegibles del OT (preasignación activa + grant vigente) para el
    // selector de la consola, en vez de que el OT escriba el tenant id manualmente.
    private static async Task<IResult> EligibleCompaniesAsync(
        HttpContext http, IPlateRangeRepository repo, CancellationToken ct)
    {
        var explicitOffice = http.Request.Query.TryGetValue("transitOfficeId", out var raw)
            && Guid.TryParse(raw, out var oid) ? (Guid?)oid : null;

        var officeId = await ResolveOfficeIdAsync(http, explicitOffice, repo, ct).ConfigureAwait(false);
        if (officeId is null)
        {
            return Results.BadRequest(new { error = "No se pudo resolver el organismo de tránsito." });
        }

        var companies = await repo.ListEligibleCompaniesAsync(officeId.Value, ct).ConfigureAwait(false);
        return Results.Ok(companies);
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

/// <summary>Payload para asignar una placa a un trámite en preasignado (Flujo B). <c>OutOfRange</c> (HU #10800)
/// registra una placa que no pertenece a ningún rango como rango ad-hoc de 1 placa.</summary>
public sealed record AssignPlateToProcedureRequest(string Plate, bool OutOfRange = false);

/// <summary>Payload para revocar la preasignación de un trámite.</summary>
public sealed record RevokePlateRequest(string Reason);

/// <summary>Logging source-generated (CA1848) de la regeneración documental tras asignar placa. Sin PII.</summary>
internal static partial class AdminPlateRegenLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudieron regenerar los documentos tras asignar la placa al trámite {InstanceId}; se conservan los previos.")]
    public static partial void RegeneracionPlacaOmitida(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Conflicto de persistencia al asignar la placa al trámite {InstanceId}; se responde 409 al OT.")]
    public static partial void AsignacionPlacaConflicto(ILogger logger, Exception ex, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo encolar el aviso de correo tras asignar la placa al trámite {InstanceId}; la asignación se conserva.")]
    public static partial void CorreoAsignacionPlacaOmitido(ILogger logger, Exception ex, Guid instanceId);
}
