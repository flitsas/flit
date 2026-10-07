using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — parámetros de
/// <c>GET /api/v1/tramites/biometric-validations/manual</c>. Todos los filtros son opcionales.
/// </summary>
public sealed record ListManualIdentityValidationsQuery(
    string? Status = null,
    string? Origin = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 20)
{
    public const int MinPageSize = 10;
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>Tope de longitud del texto libre (saneo anti-abuso, como el listado de validaciones).</summary>
    public const int TextMaxLength = 100;

    /// <summary>Estados que puede tener una validación manual (el aprobado exige origen de aprobación manual).</summary>
    public static readonly IReadOnlyList<string> AllowedStatuses =
    [
        BiometricEstados.ManualActivo,
        BiometricEstados.PendienteRevisionManual,
        BiometricEstados.Aprobado,
        BiometricEstados.Rechazado,
        BiometricEstados.Expirado,
    ];

    public int SafePage() => Page < 1 ? 1 : Page;

    public int SafePageSize() => Math.Clamp(PageSize, MinPageSize, MaxPageSize);

    /// <summary>Mensaje de error sin PII o null si los parámetros son válidos.</summary>
    public string? Validate()
    {
        if (!string.IsNullOrWhiteSpace(Status) && !AllowedStatuses.Contains(Status.Trim(), StringComparer.OrdinalIgnoreCase))
            return "estado inválido; use manual_activo, pendiente_revision_manual, aprobado, rechazado o expirado.";

        if (!string.IsNullOrWhiteSpace(Origin)
            && !ManualIdentityReviewOrigins.Todos.Contains(Origin.Trim(), StringComparer.OrdinalIgnoreCase))
            return "origen inválido; use tramite, prevalidacion, mandatario o representante_legal.";

        return null;
    }

    public ManualIdentityReviewFilter ToFilter()
    {
        var text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();
        if (text is { Length: > TextMaxLength })
            text = text[..TextMaxLength];

        return new ManualIdentityReviewFilter(
            string.IsNullOrWhiteSpace(Status) ? null : Status.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(Origin) ? null : Origin.Trim().ToLowerInvariant(),
            text);
    }
}

/// <summary>Fila del listado de validaciones manuales (contrato <c>ManualListItem</c>).</summary>
public sealed record ManualListItem(
    Guid Id,
    string FullName,
    string DocumentNumber,
    string TenantName,
    string Origin,
    string Status,
    DateTimeOffset? ActivatedAt,
    int? WaitingMinutes);

/// <summary>Respuesta paginada del listado manual.</summary>
public sealed record ManualListResponse(IReadOnlyList<ManualListItem> Items, int Total, int Page, int PageSize);

/// <summary>
/// Lista las validaciones manuales de TODAS las compañías. No comprueba el rol: el endpoint (solo Super Admin) es
/// quien lo exige antes de llegar aquí.
/// </summary>
public sealed class ListManualIdentityValidationsHandler(
    IManualIdentityReviewReadRepository repo,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<(ManualListResponse? Result, string? Error)> HandleAsync(
        ListManualIdentityValidationsQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Validate() is { } error)
            return (null, error);

        var page = query.SafePage();
        var pageSize = query.SafePageSize();
        var (rows, total) = await repo.ListAsync(query.ToFilter(), (page - 1) * pageSize, pageSize, ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        IReadOnlyList<ManualListItem> items =
        [
            .. rows.Select(r => new ManualListItem(
                r.Id, r.FullName, r.DocumentNumber, r.TenantName, r.Origin, r.Status, r.ActivatedAt,
                WaitingMinutes(r.Status, r.WaitingSince, now))),
        ];

        return (new ManualListResponse(items, total, page, pageSize), null);
    }

    /// <summary>
    /// HU #13296: minutos que lleva una validación esperando REVISIÓN (desde que el cliente envió la captura); <c>null</c> en
    /// cualquier otro estado, incluido <c>manual_activo</c> (esperar la captura del cliente no es tiempo de revisión).
    /// </summary>
    internal static int? WaitingMinutes(string status, DateTimeOffset? waitingSince, DateTimeOffset now)
    {
        if (waitingSince is not { } since || status != BiometricEstados.PendienteRevisionManual)
            return null;

        var minutes = (now - since).TotalMinutes;
        return minutes < 0 ? 0 : (int)minutes;
    }
}
