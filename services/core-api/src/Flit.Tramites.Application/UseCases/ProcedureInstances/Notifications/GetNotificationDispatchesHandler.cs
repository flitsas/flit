using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Notifications;

/// <summary>Fila de despacho de aviso de correo (correo enmascarado — HU #11470).</summary>
public sealed record NotificationDispatchItemDto(
    Guid Id,
    string RecipientRole,
    string RecipientKind,
    string? RecipientMasked,
    string? RecipientName,
    string TemplateKey,
    string Status,
    string? FailureReason,
    int Attempts,
    DateTimeOffset QueuedAt,
    DateTimeOffset? ProcessedAt);

/// <summary>Lista de despachos del trámite para el gestor.</summary>
public sealed record NotificationDispatchesDto(IReadOnlyList<NotificationDispatchItemDto> Items);

/// <summary>
/// GET /instances/{id}/notification-dispatches (HU #11470): desenlace de cada cupo de aviso
/// de correo. El correo se devuelve enmascarado; 404 si el trámite no pertenece al tenant.
/// </summary>
public sealed class GetNotificationDispatchesHandler(IProcedureInstanceRepository repo)
{
    public async Task<(NotificationDispatchesDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var rows = await repo.ListEmailDispatchesAsync(instanceId, tenantId, ct).ConfigureAwait(false);
        if (rows is null)
            return (null, "not_found");

        var items = rows.Select(Map).ToList();
        return (new NotificationDispatchesDto(items), null);
    }

    private static NotificationDispatchItemDto Map(ProcedureStateChangeEmailDispatch d) => new(
        d.Id,
        d.RecipientRole,
        d.RecipientKind,
        MaskEmail(d.Recipient),
        d.RecipientName,
        d.TemplateKey,
        d.Status,
        d.FailureReason,
        d.Attempts,
        d.QueuedAt,
        d.ProcessedAt);

    /// <summary>Enmascara local-part y dominio; el correo completo no viaja a la UI.</summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
            return "***";

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];
        var visibleLocal = local.Length == 1
            ? $"{local}***"
            : $"{local[..2]}***";

        var dot = domain.LastIndexOf('.');
        var maskedDomain = dot > 0
            ? $"***.{domain[(dot + 1)..]}"
            : "***";

        return $"{visibleLocal}@{maskedDomain}";
    }
}
