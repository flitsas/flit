using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.List;

/// <summary>
/// Consulta del historial de documentos standalone (CF-17/CF-18, HU #12204).
///
/// <para><b>El tenant nunca es opcional.</b> El comando trae el tenant del JWT y, solo para un
/// SuperAdmin, un <c>tenantId</c> explícito de otra compañía (CF-20). Un usuario normal que envíe
/// ese parámetro no amplía nada: se ignora y se consulta su propio tenant. No existe camino que
/// produzca una consulta sin <c>WHERE tenant_id</c> — el aislamiento es ese filtro, no la RLS.</para>
///
/// <para><b>Qué NO devuelve, en ningún caso y para ningún rol:</b> <c>document_snapshot</c>,
/// <c>rues_snapshot</c>, la ruta de storage o cualquier URL firmada. La proyección
/// <see cref="StandaloneDocumentListItem"/> ni siquiera tiene esos campos (CF-26 / CF-20).</para>
/// </summary>
public sealed class ListStandaloneDocumentsHandler
{
    private readonly IStandaloneDocumentRepository _repository;

    public ListStandaloneDocumentsHandler(IStandaloneDocumentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<StandaloneDocumentPage> HandleAsync(
        ListStandaloneDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // CF-20: el filtro cross-tenant es privilegio de SuperAdmin y debe ser EXPLÍCITO. Para
        // cualquier otro usuario el parámetro no existe: se cae al tenant del JWT.
        var tenantId = query.IsSuperAdmin && query.RequestedTenantId is { } solicitado && solicitado != Guid.Empty
            ? solicitado
            : query.TenantId;

        var statuses = query.Statuses?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(IsKnownStatus)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var filter = new StandaloneDocumentFilter
        {
            TenantId = tenantId,
            DocumentType = NormalizeDocumentType(query.DocumentType),
            Statuses = statuses is { Length: > 0 } ? statuses : null,
            DateFrom = query.DateFrom,
            DateTo = query.DateTo,
            CreatedByUserId = query.CreatedByUserId == Guid.Empty ? null : query.CreatedByUserId,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        return _repository.ListAsync(filter, cancellationToken);
    }

    /// <summary>
    /// Un estado desconocido se descarta en vez de propagarse al SQL: filtrar por un literal que la
    /// columna no admite devolvería «cero resultados» y parecería un historial vacío.
    /// </summary>
    private static bool IsKnownStatus(string status) => status is
        StandaloneDocumentStatus.Pending
        or StandaloneDocumentStatus.Processing
        or StandaloneDocumentStatus.Generated
        or StandaloneDocumentStatus.Error;

    private static string? NormalizeDocumentType(string? documentType)
    {
        var value = documentType?.Trim().ToLowerInvariant();
        return value is StandaloneDocumentType.CertificadoRues
            or StandaloneDocumentType.TransferenciaDominioGenerada
            ? value
            : null;
    }
}

/// <summary>
/// Entrada del historial. <see cref="TenantId"/> y <see cref="IsSuperAdmin"/> los resuelve el
/// endpoint desde el JWT; <see cref="RequestedTenantId"/> es el parámetro opcional de la petición.
/// </summary>
public sealed record ListStandaloneDocumentsQuery
{
    public required Guid TenantId { get; init; }

    public bool IsSuperAdmin { get; init; }

    public Guid? RequestedTenantId { get; init; }

    public string? DocumentType { get; init; }

    /// <summary>Estados internos. «En proceso» llega expandido a pending + processing (CF-21).</summary>
    public IReadOnlyCollection<string>? Statuses { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }

    public Guid? CreatedByUserId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
