using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>Filtros del Historial (HU #12310 AC1). <c>Search</c> busca por número de trámite, placa o VIN.</summary>
public sealed record RuntConfirmationAttemptsQuery(
    Guid? RunId,
    string? Verdict,
    string? ProcedureTypeCode,
    Guid? ProcedureInstanceId,
    string? Search,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 500;

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 50 : Math.Min(PageSize, MaxPageSize);
}

/// <summary>Una fila del Historial: lo que se lista y lo que se exporta (el motivo incluido: es uso interno).</summary>
public sealed record RuntConfirmationAttemptRow(
    Guid Id,
    DateTimeOffset QueriedAt,
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string ProcedureTypeCode,
    string ProcedureTypeName,
    string Family,
    string? Plate,
    string? Vin,
    Guid TenantId,
    string? TenantName,
    string ProviderKey,
    string QueryKind,
    int AttemptNo,
    string Verdict,
    string ReasonText,
    string RuleVersion,
    Guid? RunId,
    Guid? RequestedBy,
    string? FlagApplied,
    bool HasRaw);

public sealed record RuntConfirmationAttemptDetail(
    RuntConfirmationAttemptRow Row,
    Guid? RawPayloadId,
    Guid? SellerRawPayloadId,
    Guid? ReevaluatedFromAttemptId,
    string? ProcedureStatus,
    DateTimeOffset? RuntConfirmedAt,
    int RuntAttempts,
    string? RuntFlag);

public sealed record RuntConfirmationAttemptsPage(IReadOnlyList<RuntConfirmationAttemptRow> Items, int Total, int Page, int PageSize);

public sealed record RuntConfirmationRunsPage(IReadOnlyList<RuntConfirmationRun> Items, int Total, int Page, int PageSize);

/// <summary>Lecturas del Historial. Cross-tenant: es la pantalla de plataforma, no la de una empresa.</summary>
public interface IRuntConfirmationHistoryReader
{
    Task<RuntConfirmationAttemptsPage> ListAttemptsAsync(RuntConfirmationAttemptsQuery query, CancellationToken ct = default);
    Task<RuntConfirmationAttemptDetail?> GetAttemptAsync(Guid attemptId, CancellationToken ct = default);
    Task<RuntConfirmationRunsPage> ListRunsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<RuntConfirmationRun?> GetLatestRunAsync(CancellationToken ct = default);
}

public sealed class ListRuntConfirmationAttemptsHandler(IRuntConfirmationHistoryReader reader)
{
    public Task<RuntConfirmationAttemptsPage> HandleAsync(RuntConfirmationAttemptsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return reader.ListAttemptsAsync(query, ct);
    }
}

public sealed class GetRuntConfirmationAttemptHandler(IRuntConfirmationHistoryReader reader, IRuntConfirmationStore store)
{
    public Task<RuntConfirmationAttemptDetail?> HandleAsync(Guid attemptId, CancellationToken ct = default) =>
        reader.GetAttemptAsync(attemptId, ct);

    /// <summary>Crudo principal y, en traspaso, el del vendedor. NULL si el intento no existe o no guardó crudo.</summary>
    public async Task<(string? Primary, string? Seller)?> GetRawAsync(Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await store.GetAttemptAsync(attemptId, ct).ConfigureAwait(false);
        if (attempt is null)
            return null;

        var primary = attempt.RawPayloadId is Guid p ? await store.GetPayloadJsonAsync(p, ct).ConfigureAwait(false) : null;
        var seller = attempt.SellerRawPayloadId is Guid s ? await store.GetPayloadJsonAsync(s, ct).ConfigureAwait(false) : null;
        return (primary, seller);
    }
}

public sealed class ListRuntConfirmationRunsHandler(IRuntConfirmationHistoryReader reader)
{
    public Task<RuntConfirmationRunsPage> HandleAsync(int page, int pageSize, CancellationToken ct = default) =>
        reader.ListRunsAsync(page < 1 ? 1 : page, pageSize < 1 ? 20 : Math.Min(pageSize, 200), ct);

    public Task<RuntConfirmationRun?> LatestAsync(CancellationToken ct = default) => reader.GetLatestRunAsync(ct);
}
