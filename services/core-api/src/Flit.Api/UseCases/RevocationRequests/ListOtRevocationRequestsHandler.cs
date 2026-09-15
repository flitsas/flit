using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Application.UseCases.RevocationRequests;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.RevocationRequests;

namespace Flit.Api.UseCases.RevocationRequests;

/// <summary>
/// «Listar solicitudes de revocatoria» del lado OT (HU #12578, Feature #12565): pedido de
/// GET /api/v1/admin/ot/revocation-requests. Mismos nombres de filtro que
/// <see cref="ListRevocationRequestsQuery"/> del lado gestor a propósito (ver XML doc de aquella).
/// </summary>
public sealed record ListOtRevocationRequestsQuery(
    Guid OtTenantId,
    Guid? TransitOfficeId,
    IReadOnlyList<string>? Statuses,
    DateTimeOffset? RequestedFrom,
    DateTimeOffset? RequestedTo,
    int? Skip,
    int? Take);

/// <summary>
/// «Listar solicitudes de revocatoria» del lado OT (HU #12576/#12578, Feature #12565): bandeja
/// dedicada "Revocatorias" para el perfil operativo del organismo de tránsito.
///
/// <para>
/// <b>Bounded contexts:</b> vive en <c>Flit.Api</c> (no en <c>Flit.Admin.Application</c> ni en
/// <c>Flit.Tramites.Application</c>) porque compone los dos módulos y ninguno puede referenciar al
/// otro — MISMO criterio que <see cref="DecideRevocationRequestHandler"/> (HU #12576): resuelve el
/// organismo con <see cref="IOtClientProcedureRepository.ResolveTransitOfficeIdAsync"/> (grant/perfil
/// OT, módulo Admin) y lista con <see cref="IProcedureRevocationRequestRepository.ListForTransitOfficeAsync"/>
/// (módulo Trámites).
/// </para>
///
/// <para>
/// <b>Sin organismo resoluble</b> (tenant OT sin perfil configurado) devuelve una página vacía, NO un
/// error — mismo criterio "sin organismo, sin error" que el resto de la bandeja OT
/// (<c>GetClientProceduresFilterFieldsAsync</c>, <c>ExecuteOtScopedAsync</c>): la vista dedicada
/// degrada con elegancia en vez de romper.
/// </para>
/// </summary>
public sealed class ListOtRevocationRequestsHandler(
    IOtClientProcedureRepository otRepository,
    IProcedureRevocationRequestRepository revocationRepo)
{
    public async Task<RevocationRequestListResult> HandleAsync(
        ListOtRevocationRequestsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = RevocationRequestListPaging.NormalizeTake(query.Take);
        var skip = RevocationRequestListPaging.NormalizeSkip(query.Skip);

        var transitOfficeId = await otRepository
            .ResolveTransitOfficeIdAsync(query.OtTenantId, query.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (transitOfficeId is null)
        {
            return new RevocationRequestListResult([], 0, skip, take);
        }

        var filter = new RevocationRequestListFilter
        {
            Statuses = query.Statuses,
            RequestedFrom = query.RequestedFrom,
            RequestedTo = query.RequestedTo,
            // TransitOfficeId del filtro NO se usa aquí a propósito: el alcance del lado OT YA es el
            // organismo resuelto arriba (ver XML doc de RevocationRequestListFilter.TransitOfficeId).
            Skip = skip,
            Take = take,
        };

        var page = await revocationRepo
            .ListForTransitOfficeAsync(transitOfficeId.Value, filter, cancellationToken)
            .ConfigureAwait(false);

        return new RevocationRequestListResult(
            page.Items.Select(RevocationRequestListItemDto.From).ToList(),
            page.TotalCount,
            skip,
            take);
    }
}
