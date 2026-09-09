namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Filtros del historial de <c>admin.standalone_documents</c> (CF-17/CF-18, HU #12204).
///
/// <para><see cref="TenantId"/> es el universo visible, no un «filtro» del usuario. Tres caminos, y
/// solo uno de ellos es global:</para>
/// <list type="bullet">
///   <item>Usuario normal: el tenant del JWT. Siempre. Un <c>tenantId</c> en la query se ignora.</item>
///   <item>SuperAdmin con <c>tenantId</c> explícito: esa compañía (CF-20).</item>
///   <item>SuperAdmin que pide <b>explícitamente</b> todas: <c>null</c>, y solo entonces.</item>
/// </list>
///
/// <para><b>El tipo es anulable a propósito y sigue siendo <c>required</c>.</b> En este repo el
/// aislamiento real entre compañías es este <c>WHERE tenant_id</c> y no la RLS —no hay
/// <c>FORCE ROW LEVEL SECURITY</c> y la aplicación conecta como owner—, así que el listado global
/// no puede alcanzarse por olvido: quien construya un filtro tiene que escribir <c>null</c> a
/// conciencia. El único sitio que lo hace es <c>ListStandaloneDocumentsHandler</c>, y solo tras
/// comprobar que quien pregunta es SuperAdmin y lo pidió.</para>
///
/// <para><see cref="Statuses"/> recibe estados INTERNOS (los cuatro de
/// <see cref="StandaloneDocumentStatus"/>). La interfaz ofrece tres opciones y expande «En proceso»
/// a <c>pending</c> + <c>processing</c> antes de llamar (CF-21): la expansión es del cliente, no de
/// esta capa.</para>
/// </summary>
public sealed record StandaloneDocumentFilter
{
    /// <summary><c>null</c> = todas las compañías. Ver la nota de la clase antes de usarlo.</summary>
    public required Guid? TenantId { get; init; }

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

    /// <summary>
    /// Lote al que pertenece la fila (CF-18 en I3, HU #12211). <c>null</c> = todos los documentos,
    /// individuales y de lote.
    ///
    /// <para>Es un filtro MÁS, no un modo aparte: convive con el de tipo, el de fechas, el de
    /// usuario y el de estado, y todos se aplican a la vez con AND. La sub-decisión §3.c del diseño
    /// —una fila del XLSX ES una fila de <c>admin.standalone_documents</c>— es justamente lo que
    /// permite que filtrar por lote sea un <c>WHERE</c> más y no una consulta a otra tabla.</para>
    /// </summary>
    public Guid? BatchId { get; init; }

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
