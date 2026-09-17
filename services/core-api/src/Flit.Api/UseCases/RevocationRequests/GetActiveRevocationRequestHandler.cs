using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Api.UseCases.RevocationRequests;

public sealed record GetActiveRevocationRequestQuery(
    Guid OtTenantId,
    Guid ProcedureInstanceId,
    Guid? TransitOfficeId);

public enum GetActiveRevocationRequestStatus
{
    Found,
    ProcedureNotFound,
    RequestNotFound,
}

public sealed record ActiveRevocationRequestDetail(
    Guid RevocationRequestId,
    int AttemptNumber,
    string? Reason,
    Guid? SupportDocumentId,
    DateTimeOffset RequestedAt);

public sealed class GetActiveRevocationRequestResult
{
    public GetActiveRevocationRequestStatus Status { get; init; }

    public ActiveRevocationRequestDetail? Detail { get; init; }

    public static GetActiveRevocationRequestResult Found(ActiveRevocationRequestDetail detail) =>
        new() { Status = GetActiveRevocationRequestStatus.Found, Detail = detail };

    public static GetActiveRevocationRequestResult ProcedureNotFound() =>
        new() { Status = GetActiveRevocationRequestStatus.ProcedureNotFound };

    public static GetActiveRevocationRequestResult RequestNotFound() =>
        new() { Status = GetActiveRevocationRequestStatus.RequestNotFound };
}

/// <summary>
/// Feature #12565 — lectura del motivo + documento de soporte de la solicitud de revocatoria ACTIVA de
/// un trámite, para el modal "Decidir revocatoria" del OT. Antes de este handler ninguna ruta OT
/// exponía ese motivo ni el PDF que cargó el gestor (<c>RequestRevocationHandler</c>, HU #12572): el OT
/// aprobaba/rechazaba sin poder verlos. Reutiliza el MISMO acceso cross-tenant que
/// <see cref="DecideRevocationRequestHandler"/> — resuelve el tenant cliente dueño del trámite vía el
/// grant vigente del OT y lee la solicitud dentro del scope RLS de ese tenant.
/// </summary>
public sealed class GetActiveRevocationRequestHandler(
    IOtClientProcedureRepository otRepository,
    IProcedureRevocationRequestRepository revocationRepo)
{
    public async Task<GetActiveRevocationRequestResult> HandleAsync(
        GetActiveRevocationRequestQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var access = await otRepository
            .GetByIdAsync(query.OtTenantId, query.ProcedureInstanceId, query.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        if (access is null)
            return GetActiveRevocationRequestResult.ProcedureNotFound();

        var activeRequest = await otRepository.ExecuteInClientTenantScopeAsync(
            access.ClientTenantId,
            () => revocationRepo.FindActiveAsync(access.ClientTenantId, query.ProcedureInstanceId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (activeRequest is null)
            return GetActiveRevocationRequestResult.RequestNotFound();

        return GetActiveRevocationRequestResult.Found(new ActiveRevocationRequestDetail(
            activeRequest.Id,
            activeRequest.AttemptNumber,
            activeRequest.Reason,
            activeRequest.SupportDocumentId,
            activeRequest.RequestedAt));
    }
}
