using System.Text.Json;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.BulkTramites.SubmitBatch;

/// <summary>
/// Orquesta la carga de un lote (HU #12522, AC1/AC2/AC3): parsea el archivo contra la plantilla
/// de su tipo y, si el ARCHIVO es estructuralmente válido, persiste el lote y sus filas —una fila
/// con error de porcentaje se guarda igual, marcada, y queda fuera de la cola de HU #12523.
/// </summary>
public sealed class SubmitBulkTramitesBatchHandler(
    IBulkTramitesXlsxParser parser,
    IBulkTramitesBatchRepository repository,
    TimeProvider clock)
{
    private static readonly Dictionary<BulkTramitesTemplateType, string> TemplateTypeCodes = new()
    {
        [BulkTramitesTemplateType.Matricula] = "matricula",
        [BulkTramitesTemplateType.Traspaso] = "traspaso",
        [BulkTramitesTemplateType.Otros] = "otros",
    };

    public async Task<SubmitBulkTramitesBatchResult> HandleAsync(
        SubmitBulkTramitesBatchCommand command, CancellationToken ct = default)
    {
        var parsed = parser.Parse(command.TemplateType, command.Content);
        if (parsed.FileError is not null)
        {
            return SubmitBulkTramitesBatchResult.Rejected(parsed.FileError);
        }

        if (parsed.Rows.Count == 0)
        {
            return SubmitBulkTramitesBatchResult.Rejected("empty_file");
        }

        var now = clock.GetUtcNow();
        var batch = new BulkTramitesBatch
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            CreatedByUserId = command.CreatedByUserId,
            TemplateType = TemplateTypeCodes[command.TemplateType],
            Status = BulkTramitesBatchStatus.Queued,
            SourceFilename = command.SourceFilename,
            TotalRows = parsed.Rows.Count,
            RowsWithStructuralErrors = parsed.Rows.Count(r => r.StructuralErrorCode is not null),
            CreatedAt = now,
        };

        foreach (var fila in parsed.Rows)
        {
            batch.Rows.Add(new BulkTramitesBatchRow
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                RowNumber = fila.RowNumber,
                ValuesJson = JsonSerializer.Serialize(fila.Values),
                StructuralErrorCode = fila.StructuralErrorCode,
                CreatedAt = now,
            });
        }

        await repository.AddAsync(batch, ct).ConfigureAwait(false);
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SubmitBulkTramitesBatchResult(
            SubmitBulkTramitesBatchOutcome.Accepted,
            batch.Id,
            batch.TotalRows,
            batch.RowsWithStructuralErrors);
    }
}
