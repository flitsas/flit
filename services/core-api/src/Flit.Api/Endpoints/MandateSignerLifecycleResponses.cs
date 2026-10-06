using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13134–#13137 (Feature #13115) — cuerpo HTTP único de baja, eliminación, impacto y reactivación de
/// mandatarios, y la orquestación de las rutas de COMPAÑÍA (propias y de hijas de la red), para que las dos
/// superficies respondan exactamente igual. Nada aquí lleva datos personales: solo identificadores y conteos.
/// </summary>
internal static class MandateSignerLifecycleResponses
{
    public const string ConfirmationRequiredCode = "mandatario_baja_requiere_confirmacion";
    public const string ReassignedHeader = "X-Mandatario-Reasignados";
    public const string PendingHeader = "X-Mandatario-Pendientes-Decision-OT";

    public static object ImpactBody(MandateSignerImpact impact) => new
    {
        hasImpact = !impact.IsEmpty,
        onlyActiveFor = impact.OnlyActiveFor.Select(l => new
        {
            transitOfficeId = l.TransitOfficeId,
            companyTenantId = l.CompanyTenantId,
        }),
        defaults = impact.Defaults.Select(d => new
        {
            kind = d.Kind,
            transitOfficeId = d.TransitOfficeId,
            companyTenantId = d.CompanyTenantId,
        }),
        pendingProcedures = impact.PendingProcedures,
    };

    /// <summary>204 + conteos de la reasignación de trámites en cabeceras (el 204 no lleva cuerpo).</summary>
    public static IResult NoContentWithCounts(HttpContext http, MandateSignerLifecycleResult? lifecycle)
    {
        if (lifecycle is not null)
        {
            http.Response.Headers[ReassignedHeader] = lifecycle.Reassignment.Reassigned.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            http.Response.Headers[PendingHeader] = lifecycle.Reassignment.Pending.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return Results.NoContent();
    }

    public static IResult ForInactivate(HttpContext http, InactivateMandateSignerResult result, string notFoundMessage) =>
        result.Outcome == InactivateMandateSignerOutcome.Inactivated
            ? NoContentWithCounts(http, result.Lifecycle)
            : Results.NotFound(new { error = notFoundMessage });

    public static IResult ForDelete(HttpContext http, DeleteMandateSignerResult result, string notFoundMessage) =>
        result.Outcome switch
        {
            DeleteMandateSignerOutcome.Deleted => NoContentWithCounts(http, result.Lifecycle),
            DeleteMandateSignerOutcome.ConfirmationRequired => Results.Json(
                new
                {
                    code = ConfirmationRequiredCode,
                    error = "La baja afecta defaults, compañías o trámites: confirme el impacto para continuar.",
                    impact = ImpactBody(result.Impact ?? MandateSignerImpact.Empty),
                },
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound(new { error = notFoundMessage }),
        };

    public static IResult ForReactivate(ReactivateMandateSignerResult result, string notFoundMessage)
    {
        switch (result.Outcome)
        {
            case ReactivateMandateSignerOutcome.Reactivated:
                var lifecycle = result.Lifecycle!;
                return Results.Ok(new
                {
                    restoredLinks = LinksBody(lifecycle.RestoredLinks),
                    conflictLinks = LinksBody(lifecycle.ConflictLinks),
                    restoredDefaults = lifecycle.RestoredDefaults,
                });
            case ReactivateMandateSignerOutcome.Conflict:
                return Results.Json(
                    new
                    {
                        code = MandateSignerLinkConflictFilter.ErrorCode,
                        error = MandateSignerActiveLinkConflictException.DefaultMessage,
                        conflictLinks = LinksBody(result.Lifecycle?.ConflictLinks ?? []),
                    },
                    statusCode: StatusCodes.Status409Conflict);
            default:
                return Results.NotFound(new { error = notFoundMessage });
        }
    }

    private static IEnumerable<object> LinksBody(IReadOnlyList<MandateSignerLinkRef> links) =>
        links.Select(l => new { transitOfficeId = l.TransitOfficeId, companyTenantId = l.CompanyTenantId });

    // ── Rutas de COMPAÑÍA (propias y de hijas): una sola implementación ─────────────────────────────

    private static string NotInCompany(Guid signerId) => $"No existe el mandatario {signerId} en esta compañía.";

    /// <summary>
    /// Organismo con el que atribuir la auditoría de la baja o alta lógica. Se toma del propio mandatario —no de la
    /// petición— y de paso confirma que pertenece a esta compañía.
    /// </summary>
    public static async Task<Guid?> ResolvePrimaryOfficeAsync(
        ListCompanyMandateSignersHandler listHandler,
        Guid tenantId,
        Guid mandateSignerId,
        CancellationToken cancellationToken)
    {
        var signers = await listHandler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return signers.FirstOrDefault(s => s.Id == mandateSignerId)?.TransitOfficeId;
    }

    public static async Task<IResult> CompanyInactivateAsync(
        HttpContext http,
        Guid tenantId,
        Guid mandateSignerId,
        MandateSignerAccessGuard guard,
        ListCompanyMandateSignersHandler listHandler,
        InactivateMandateSignerHandler handler,
        CancellationToken ct)
    {
        var denied = await MandateSignerActors
            .CheckCompanyWriteAsync(guard, http.User, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        var officeId = await ResolvePrimaryOfficeAsync(listHandler, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (officeId is null)
        {
            return Results.NotFound(new { error = NotInCompany(mandateSignerId) });
        }

        var result = await handler.HandleDetailedAsync(
            new InactivateMandateSignerCommand
            {
                TransitOfficeId = officeId.Value,
                MandateSignerId = mandateSignerId,
                ChangedBy = AdminMandateSignerUser.Id(http.User),
                ActorKind = MandateSignerActors.ForCompany(http.User),
            },
            ct).ConfigureAwait(false);

        return ForInactivate(http, result, NotInCompany(mandateSignerId));
    }

    public static async Task<IResult> CompanyReactivateAsync(
        HttpContext http,
        Guid tenantId,
        Guid mandateSignerId,
        MandateSignerAccessGuard guard,
        ListCompanyMandateSignersHandler listHandler,
        ReactivateMandateSignerHandler handler,
        CancellationToken ct)
    {
        var denied = await MandateSignerActors
            .CheckCompanyWriteAsync(guard, http.User, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        var officeId = await ResolvePrimaryOfficeAsync(listHandler, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (officeId is null)
        {
            return Results.NotFound(new { error = NotInCompany(mandateSignerId) });
        }

        var result = await handler.HandleDetailedAsync(
            new ReactivateMandateSignerCommand
            {
                TransitOfficeId = officeId.Value,
                MandateSignerId = mandateSignerId,
                ChangedBy = AdminMandateSignerUser.Id(http.User),
                ActorKind = MandateSignerActors.ForCompany(http.User),
            },
            ct).ConfigureAwait(false);

        return ForReactivate(result, NotInCompany(mandateSignerId));
    }

    public static async Task<IResult> CompanyDeleteAsync(
        HttpContext http,
        Guid tenantId,
        Guid mandateSignerId,
        bool confirmarImpacto,
        MandateSignerAccessGuard guard,
        ListCompanyMandateSignersHandler listHandler,
        DeleteMandateSignerHandler handler,
        CancellationToken ct)
    {
        var denied = await MandateSignerActors
            .CheckCompanyWriteAsync(guard, http.User, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        var officeId = await ResolvePrimaryOfficeAsync(listHandler, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (officeId is null)
        {
            return Results.NotFound(new { error = NotInCompany(mandateSignerId) });
        }

        var result = await handler.HandleAsync(
            new DeleteMandateSignerCommand
            {
                TransitOfficeId = officeId.Value,
                MandateSignerId = mandateSignerId,
                ConfirmImpact = confirmarImpacto,
                ChangedBy = AdminMandateSignerUser.Id(http.User),
                ActorKind = MandateSignerActors.ForCompany(http.User),
            },
            ct).ConfigureAwait(false);

        return ForDelete(http, result, NotInCompany(mandateSignerId));
    }

    public static async Task<IResult> CompanyImpactAsync(
        HttpContext http,
        Guid tenantId,
        Guid mandateSignerId,
        MandateSignerAccessGuard guard,
        ListCompanyMandateSignersHandler listHandler,
        GetMandateSignerImpactHandler handler,
        CancellationToken ct)
    {
        var denied = await MandateSignerActors
            .CheckCompanyWriteAsync(guard, http.User, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        if (denied is not null)
        {
            return denied;
        }

        var officeId = await ResolvePrimaryOfficeAsync(listHandler, tenantId, mandateSignerId, ct).ConfigureAwait(false);
        var impact = officeId is null
            ? null
            : await handler.HandleAsync(officeId.Value, mandateSignerId, ct).ConfigureAwait(false);

        return impact is null
            ? Results.NotFound(new { error = NotInCompany(mandateSignerId) })
            : Results.Ok(new { data = ImpactBody(impact) });
    }
}

/// <summary>Id del usuario autenticado (claim <c>sub</c> o <c>NameIdentifier</c>) para atribuir la bitácora.</summary>
internal static class AdminMandateSignerUser
{
    public static Guid? Id(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
