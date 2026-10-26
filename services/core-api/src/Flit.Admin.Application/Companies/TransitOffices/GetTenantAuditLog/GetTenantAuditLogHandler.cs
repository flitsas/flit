using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>
/// Caso de uso de la consulta paginada del historial de auditoría de gobernanza
/// (HU #10192, AC4). Normaliza la paginación (mismo criterio que el listado de
/// compañías) y delega la lectura ordenada por <c>changed_at</c> DESC al repositorio.
/// </summary>
public sealed class GetTenantAuditLogHandler
{
    /// <summary>Página por defecto cuando no se informa o es inválida.</summary>
    public const int DefaultPage = 1;

    /// <summary>Tamaño de página por defecto.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de tamaño de página para proteger la base de datos.</summary>
    public const int MaxPageSize = 100;

    private readonly ITenantAuditLogRepository _repository;

    public GetTenantAuditLogHandler(ITenantAuditLogRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetTenantAuditLogResult> HandleAsync(
        GetTenantAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);

        var filter = BuildFilter(query);

        var result = await _repository
            .ListPagedAsync(query.TenantId, page, pageSize, filter, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AuditLogEntryResponse> data =
        [
            .. result.Items.Select(e => new AuditLogEntryResponse(
                e.EntityName, e.FieldName, e.OldValue, e.NewValue, e.ChangedBy, e.ChangedAt)),
        ];

        return new GetTenantAuditLogResult(data, result.TotalCount, page, pageSize);
    }

    /// <summary>Mensaje de validación cuando el rango de fechas está invertido.</summary>
    public const string InvalidDateRangeMessage = "La fecha desde no puede ser posterior a la fecha hasta.";

    // Colombia no tiene horario de verano: offset fijo UTC-5 (la API compila con
    // InvariantGlobalization y no puede depender de TimeZoneInfo "America/Bogota").
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    private static TenantAuditLogFilter BuildFilter(GetTenantAuditLogQuery query)
    {
        if (query.FechaDesde is { } d && query.FechaHasta is { } h && d > h)
        {
            throw new InvalidAuditLogFilterException(
                [new CompanyValidationError("fechaDesde", InvalidDateRangeMessage)]);
        }

        DateTimeOffset? from = query.FechaDesde is { } desde
            ? new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), BogotaOffset)
            : null;

        // Inclusivo del día completo: < 00:00 local del día siguiente.
        DateTimeOffset? toExclusive = query.FechaHasta is { } hasta
            ? new DateTimeOffset(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), BogotaOffset)
            : null;

        return new TenantAuditLogFilter(
            from,
            toExclusive,
            Normalize(query.Campo),
            Normalize(query.ValorAnterior),
            Normalize(query.Operador));
    }

    private static string? Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
}
