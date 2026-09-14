using System.Text.Json;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.BulkTramites.Query;

/// <summary>Contadores del lote, ya resueltos: el frontend no tiene por qué agrupar filas (HU #12524).</summary>
public sealed record BulkTramitesBatchCounts(
    int Created,
    int CreatedPending,
    int NotCreated,
    int Pendientes);

public sealed record BulkTramitesBatchSummaryDto(
    Guid Id,
    string TemplateType,
    string SourceFilename,
    string Status,
    int TotalRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    BulkTramitesBatchCounts Counts);

/// <summary>
/// Una fila en el resumen. <see cref="Identificador"/> es la placa o el VIN que escribió el
/// usuario: es por lo que reconoce la fila, mucho antes que por su número.
/// </summary>
public sealed record BulkTramitesBatchRowDto(
    int RowNumber,
    string? Identificador,
    string? Outcome,
    string? Motivo,
    Guid? ProcedureInstanceId);

public sealed record BulkTramitesBatchDetailDto(
    BulkTramitesBatchSummaryDto Batch,
    IReadOnlyList<BulkTramitesBatchRowDto> Rows);

/// <summary>
/// Resumen de los lotes de carga masiva del tenant (HU #12524). Lo consulta /tramites para
/// enseñar en qué quedó cada fila — incluidas las que hay que retomar en el wizard.
/// </summary>
public sealed class GetBulkTramitesBatchesHandler(IBulkTramitesBatchRepository repository)
{
    /// <summary>Tope de lotes que se listan. Es un resumen reciente, no un histórico paginado.</summary>
    public const int MaxBatches = 10;

    public async Task<IReadOnlyList<BulkTramitesBatchSummaryDto>> ListAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var lotes = await repository.ListByTenantAsync(tenantId, MaxBatches, ct).ConfigureAwait(false);
        return [.. lotes.Select(Summary)];
    }

    /// <summary>Detalle fila a fila. Devuelve null si el lote no existe o es de otro tenant.</summary>
    public async Task<BulkTramitesBatchDetailDto?> GetAsync(
        Guid tenantId, Guid batchId, CancellationToken ct = default)
    {
        var lote = await repository.GetByIdAsync(batchId, ct).ConfigureAwait(false);

        // El filtro por tenant va aquí y no en la consulta para que un id de otra empresa se vea
        // igual que uno inexistente: un 404 que distingue ambos casos confirma que el lote existe.
        if (lote is null || lote.TenantId != tenantId)
        {
            return null;
        }

        var filas = lote.Rows
            .OrderBy(r => r.RowNumber)
            .Select(r => new BulkTramitesBatchRowDto(
                r.RowNumber,
                Identificador(r),
                // Una fila rechazada al subir el archivo nunca entra a la cola: para el usuario ES
                // «no creado» (así la cuenta el resumen); sin esto se rotulaba «En cola» para siempre.
                r.Outcome ?? (r.StructuralErrorCode is not null ? BulkTramitesRowOutcome.NotCreated : null),
                r.OutcomeReason ?? r.StructuralErrorCode,
                r.ProcedureInstanceId))
            .ToList();

        return new BulkTramitesBatchDetailDto(Summary(lote), filas);
    }

    private static BulkTramitesBatchSummaryDto Summary(BulkTramitesBatch lote) => new(
        lote.Id,
        lote.TemplateType,
        lote.SourceFilename,
        lote.Status,
        lote.TotalRows,
        lote.CreatedAt,
        lote.CompletedAt,
        new BulkTramitesBatchCounts(
            lote.Rows.Count(r => r.Outcome == BulkTramitesRowOutcome.Created),
            lote.Rows.Count(r => r.Outcome == BulkTramitesRowOutcome.CreatedPending),
            lote.Rows.Count(r => r.Outcome == BulkTramitesRowOutcome.NotCreated
                || r.StructuralErrorCode is not null),
            lote.Rows.Count(r => r.Outcome is null && r.StructuralErrorCode is null)));

    /// <summary>Placa o VIN de la fila, leídos del JSON que guardó el parser.</summary>
    private static string? Identificador(BulkTramitesBatchRow row)
    {
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.ValuesJson);
            if (values is null)
            {
                return null;
            }

            var placa = Valor(values, "placa");
            return placa ?? Valor(values, "vin");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Valor(Dictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}
