using Flit.Admin.Domain.Auditing;

namespace Flit.Admin.Application.Auditing.GetAdminAuditLog;

/// <summary>
/// Caso de uso de la consulta global (cross-tenant) del rastro unificado de auditoría
/// administrativo/seguridad, exclusiva de SuperAdmin (HU #10679). Normaliza la
/// paginación (mismo criterio que <c>GetTenantAuditLogHandler</c>) y delega el filtrado
/// ordenado por <c>changed_at</c> DESC al repositorio.
/// </summary>
public sealed class GetAdminAuditLogHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly IAdminAuditLogRepository _repository;

    public GetAdminAuditLogHandler(IAdminAuditLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetAdminAuditLogResult> HandleAsync(
        GetAdminAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = new AdminAuditLogFilter
        {
            UserId = query.UserId,
            TenantId = query.TenantId,
            TenantType = Trim(query.TenantType),
            Module = Trim(query.Module),
            Operation = Trim(query.Operation),
            Result = Trim(query.Result),
            DateFrom = query.DateFrom,
            DateTo = query.DateTo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _repository.ListPagedAsync(filter, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AdminAuditLogEntryResponse> data =
        [
            .. result.Items.Select(e => new AdminAuditLogEntryResponse(
                e.Id,
                e.TenantId,
                e.TenantType,
                e.Module,
                e.EntityName,
                e.Operation,
                e.Result,
                e.ErrorCode,
                e.ChangedBy,
                e.TargetEntityType,
                e.TargetEntityId,
                e.ClientIp,
                e.ChangedAt,
                e.ChangedByName,
                e.ChangedByEmail,
                e.TargetName,
                e.FieldName,
                e.OldValue,
                e.NewValue)),
        ];

        return new GetAdminAuditLogResult(data, result.TotalCount, page, pageSize);
    }

    private static int NormalizePage(int? page) =>
        page is null || page < 1 ? DefaultPage : page.Value;

    private static int NormalizePageSize(int? pageSize)
    {
        if (pageSize is null || pageSize < 1)
        {
            return DefaultPageSize;
        }

        return pageSize.Value > MaxPageSize ? MaxPageSize : pageSize.Value;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
