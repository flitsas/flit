namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Filtros del historial de <c>admin.standalone_documents</c> (CF-17/CF-18, HU #12204).
///
/// <para><see cref="TenantId"/> NO es opcional y no viaja como «filtro» del usuario: es el universo
/// visible. Un SuperAdmin que quiera ver otra compañía envía su <c>tenantId</c> explícito y el
/// endpoint lo resuelve a este campo; nunca existe una consulta sin tenant (§5.4 del diseño: el
/// aislamiento real es este <c>WHERE</c>, no la política RLS).</para>
///
/// <para><see cref="Statuses"/> recibe estados INTERNOS (los cuatro de
/// <see cref="StandaloneDocumentStatus"/>). La interfaz ofrece tres opciones y expande «En proceso»
/// a <c>pending</c> + <c>processing</c> antes de llamar (CF-21): la expansión es del cliente, no de
/// esta capa.</para>
/// </summary>
public sealed record StandaloneDocumentFilter
{
    public required Guid TenantId { get; init; }

    /// <summary>Uno de <see cref="StandaloneDocumentType"/>. <c>null</c> = todos.</summary>
    public string? DocumentType { get; init; }

    /// <summary>Estados internos a incluir. <c>null</c> o vacío = todos.</summary>
    public IReadOnlyCollection<string>? Statuses { get; init; }

    /// <summary>Límite inferior inclusivo sobre <c>created_at</c>.</summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>Límite superior EXCLUSIVO sobre <c>created_at</c> (el endpoint suma un día si el
    /// usuario envió una fecha sin hora, para que «hasta el 9» incluya el 9 completo).</summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>Autor de la generación (<c>created_by_user_id</c>). <c>null</c> = todos.</summary>
    public Guid? CreatedByUserId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Fila del historial. <b>Deliberadamente NO tiene <c>DocumentSnapshot</c> ni <c>RuesSnapshot</c>
/// ni <c>StoragePath</c></b>: el snapshot es PII alta (CF-26) y no se expone en listados, y la ruta
/// de storage no sale nunca del backend. Lo que alimenta la tabla es esta proyección pobre en PII,
/// no la entidad completa.
/// </summary>
public sealed record StandaloneDocumentListItem
{
    public required Guid Id { get; init; }

    public required string DocumentType { get; init; }

    public string? Scenario { get; init; }

    public required string Status { get; init; }

    public string? ErrorCode { get; init; }

    public string? Filename { get; init; }

    /// <summary>Razón social de la compañía dueña del documento (metadata, no PII de persona).</summary>
    public string? CompanyName { get; init; }

    public Guid CreatedByUserId { get; init; }

    /// <summary>Nombre visible del autor. Metadata de auditoría exigida por CF-17.</summary>
    public string? CreatedByUserName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Página del historial: filas + coordenadas de paginación.</summary>
public sealed record StandaloneDocumentPage(
    IReadOnlyList<StandaloneDocumentListItem> Items,
    int Page,
    int PageSize,
    int Total);
