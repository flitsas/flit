using System.Text.Json;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>Fila del historial de transiciones expuesta por la API (RF05).</summary>
public sealed record StatusHistoryItemDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    Guid? ChangedByUserId,
    string? ChangedByName,
    string? Reason);

/// <summary>Página del historial: más reciente primero.</summary>
public sealed record StatusHistoryPageDto(
    IReadOnlyList<StatusHistoryItemDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// GET /instances/{id}/status-history (HU-2, RF05): historial de transiciones de estado de la
/// instancia, paginado y ordenado por <c>changed_at</c> DESC, con el nombre del usuario que
/// ejecutó cada transición resuelto contra <c>identity.users</c> (null si fue un proceso
/// automático o el usuario ya no existe).
/// </summary>
public sealed class GetStatusHistoryHandler(IProcedureInstanceRepository repo)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<(StatusHistoryPageDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var pageData = await repo.GetStatusHistoryPageAsync(id, tenantId, (page - 1) * pageSize, pageSize, ct);
        if (pageData is null)
            return (null, TramiteEstadoErrores.NoEncontrado);

        var (entries, total) = pageData.Value;
        var items = entries
            .Select(e => new StatusHistoryItemDto(
                e.Id, e.FromStatus, e.ToStatus, e.ChangedAt, e.ChangedByUserId, ResolveActorName(e), e.Reason))
            .ToList();

        return (new StatusHistoryPageDto(items, total, page, pageSize), null);
    }

    private const string MigrationOrigin = "migration_v1";

    /// <summary>
    /// Migración V1→V2 — para eventos importados desde V1 (marcados con <c>origen=migration_v1</c> en
    /// el metadata) muestra el usuario REAL de V1 (<c>metadata.usuario</c>) en vez del usuario de sistema
    /// "Migración V1" resuelto por <c>changed_by</c>. Así el historial se lee transparente/idéntico a uno
    /// nativo. Para eventos nativos (o migración sin usuario) cae al nombre ya resuelto. No se altera
    /// <c>ChangedByUserId</c>: la trazabilidad técnica del changed_by real se conserva.
    /// </summary>
    private static string? ResolveActorName(ProcedureInstanceStatusHistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Metadata))
            return entry.ChangedByName;

        try
        {
            using var doc = JsonDocument.Parse(entry.Metadata);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("origen", out var origen)
                && origen.ValueKind == JsonValueKind.String
                && string.Equals(origen.GetString(), MigrationOrigin, StringComparison.Ordinal)
                && root.TryGetProperty("usuario", out var usuario)
                && usuario.ValueKind == JsonValueKind.String)
            {
                var real = usuario.GetString();
                if (!string.IsNullOrWhiteSpace(real))
                    return real;
            }
        }
        catch (JsonException)
        {
            // Metadata no parseable: se preserva el nombre resuelto por changed_by.
        }

        return entry.ChangedByName;
    }
}
