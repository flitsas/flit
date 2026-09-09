using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.List;

/// <summary>
/// Consulta del historial de documentos standalone (CF-17/CF-18, HU #12204).
///
/// <para><b>Aquí se decide el universo visible, y es el único sitio donde puede ser global.</b> El
/// comando trae el tenant del JWT y, solo para un SuperAdmin, o bien un <c>tenantId</c> explícito de
/// otra compañía (CF-20) o bien <c>AllTenants</c> para verlas todas. Un usuario normal que envíe
/// cualquiera de los dos no amplía nada: se ignoran y se consulta su propio tenant.</para>
///
/// <para><b>Lo global exige las dos cosas a la vez</b> —ser SuperAdmin y haberlo pedido—, nunca se
/// alcanza por omisión y no es el estado por defecto de la interfaz: cruzar datos entre compañías es
/// siempre un acto deliberado. Importa porque en este repo el aislamiento real es este
/// <c>WHERE tenant_id</c> y no la RLS (sin <c>FORCE ROW LEVEL SECURITY</c>, la app conecta como
/// owner).</para>
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
        // cualquier otro usuario ninguno de los dos parámetros existe: se cae al tenant del JWT.
        //
        // El orden importa. `AllTenants` gana sobre `RequestedTenantId` porque «todas» es una
        // petición más amplia y explícita; si llegaran los dos, quedarse con la compañía concreta
        // devolvería MENOS de lo pedido sin decirlo, que es la clase de silencio que confunde.
        Guid? tenantId;
        if (query.IsSuperAdmin && query.AllTenants)
        {
            tenantId = null;
        }
        else if (query.IsSuperAdmin && query.RequestedTenantId is { } solicitado && solicitado != Guid.Empty)
        {
            tenantId = solicitado;
        }
        else
        {
            tenantId = query.TenantId;
        }

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
            // CF-18 en I3 (HU #12211): filtro por lote, en AND con los demás.
            BatchId = query.BatchId == Guid.Empty ? null : query.BatchId,
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

    /// <summary>
    /// Solo lo honra un SuperAdmin: consulta TODAS las compañías, sin <c>WHERE tenant_id</c>. Gana
    /// sobre <see cref="RequestedTenantId"/> si llegan los dos. Para cualquier otro usuario se
    /// ignora, igual que <see cref="RequestedTenantId"/>.
    /// </summary>
    public bool AllTenants { get; init; }

    public string? DocumentType { get; init; }

    /// <summary>Estados internos. «En proceso» llega expandido a pending + processing (CF-21).</summary>
    public IReadOnlyCollection<string>? Statuses { get; init; }

    public DateTimeOffset? DateFrom { get; init; }

    public DateTimeOffset? DateTo { get; init; }

    public Guid? CreatedByUserId { get; init; }

    /// <summary>Lote del que provienen las filas (CF-18 en I3). <c>null</c> = todos.</summary>
    public Guid? BatchId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
