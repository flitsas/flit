using Flit.Tramites.Domain.Tramites.Enums;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

/// <summary>
/// Importación masiva de trámites en estado BORRADOR desde un archivo Excel/CSV. Cada fila crea un
/// borrador mínimo (modalidad o código del tipo + oficina opcional) y, opcionalmente, siembra la
/// placa/VIN como field_values para que el borrador sea identificable en la lista. El resto del
/// trámite (actores, documentos, comercial, prenda, biometría, firma) se completa después en el
/// wizard.
///
/// Orquesta casos de uso existentes (no reimplementa reglas): <see cref="CreateProcedureInstanceHandler"/>
/// para el alta (referencia, gating, snapshot) y <see cref="PatchFieldValuesHandler"/> para la seed.
/// Las filas son independientes: un error en una NO aborta el lote (partial success).
/// </summary>
public sealed class BulkImportProcedureInstancesHandler(
    CreateProcedureInstanceHandler createHandler,
    PatchFieldValuesHandler patchHandler,
    ITransitOfficeCodeResolver transitOfficeResolver,
    IMatriculaInicialGate matriculaGate,
    ILogger<BulkImportProcedureInstancesHandler> logger)
{
    public async Task<ProcedureImportReport> HandleAsync(
        Guid tenantId,
        Guid createdByUserId,
        IReadOnlyList<ProcedureImportRow> rows,
        CancellationToken ct = default)
    {
        var results = new List<ProcedureImportRowResult>(rows.Count);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            // Cada fila es independiente: una excepción inesperada (p. ej. un fallo de BD) se reporta
            // como fila fallida y NO aborta el lote ni devuelve 500. La cancelación sí se propaga.
            try
            {
                results.Add(await ImportRowAsync(tenantId, createdByUserId, row, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                BulkImportLog.RowFailed(logger, ex, row.RowNumber);
                results.Add(new ProcedureImportRowResult(
                    row.RowNumber,
                    row.TipoCodigo ?? row.Modalidad ?? "(sin modalidad/tipo)",
                    "failed",
                    null,
                    null,
                    "error_interno"));
            }
        }

        var created = results.Count(r => r.Status == "created");
        return new ProcedureImportReport(
            Total: results.Count,
            Created: created,
            Failed: results.Count - created,
            Results: results);
    }

    private async Task<ProcedureImportRowResult> ImportRowAsync(
        Guid tenantId,
        Guid createdByUserId,
        ProcedureImportRow row,
        CancellationToken ct)
    {
        var input = row.TipoCodigo ?? row.Modalidad ?? "(sin modalidad/tipo)";

        ProcedureImportRowResult Failed(string error) =>
            new(row.RowNumber, input, "failed", null, null, error);

        // Resolver la oficina de tránsito por código (opcional). Código presente pero inexistente → error de fila.
        Guid? transitOfficeId = null;
        if (!string.IsNullOrWhiteSpace(row.TransitOfficeCode))
        {
            transitOfficeId = transitOfficeResolver.ResolveId(row.TransitOfficeCode.Trim());
            if (transitOfficeId is null)
                return Failed("oficina_no_encontrada");
        }

        // Paridad con el endpoint de create: la matrícula inicial requiere el toggle de la compañía.
        // Solo se evalúa en la ruta por modalidad (igual que ProcedureInstanceEndpoints).
        if (EsMatriculaInicial(row.Modalidad) && !await matriculaGate.IsAllowedAsync(tenantId, ct))
            return Failed("matricula_inicial_no_habilitada");

        var request = new CreateProcedureInstanceRequest(
            TenantId: tenantId,
            ProcedureTypeId: null,
            CreatedByUserId: createdByUserId,
            TransitOfficeId: transitOfficeId,
            Modalidad: row.Modalidad,
            ProcedureTypeCode: row.TipoCodigo);

        var (summary, error) = await createHandler.HandleAsync(request, ct);
        if (error is not null || summary is null)
            return Failed(error ?? "create_failed");

        // Seed opcional identificable (mismo canal que el front en /tramites/nuevo/[modalidad]).
        var seedError = await SeedIdentifiersAsync(summary.Id, tenantId, row, ct);

        return new ProcedureImportRowResult(
            row.RowNumber,
            input,
            "created",
            summary.ReferenceNumber,
            summary.Id,
            seedError is null ? null : $"seed_warning:{seedError}");
    }

    /// <summary>Persiste placa/VIN como field_values si vienen. No bloquea el alta (solo avisa).</summary>
    private async Task<string?> SeedIdentifiersAsync(
        Guid instanceId,
        Guid tenantId,
        ProcedureImportRow row,
        CancellationToken ct)
    {
        var items = new List<FieldValueInput>(2);
        if (!string.IsNullOrWhiteSpace(row.Vin))
            items.Add(new FieldValueInput(null, "vin", row.Vin.Trim(), null));
        if (!string.IsNullOrWhiteSpace(row.Placa))
            items.Add(new FieldValueInput(null, "plate", row.Placa.Trim(), null));

        if (items.Count == 0)
            return null;

        var (_, error) = await patchHandler.HandleAsync(
            instanceId, tenantId, new PatchFieldValuesRequest(items), ct);
        return error;
    }

    private static bool EsMatriculaInicial(string? modalidad) =>
        string.Equals(
            modalidad?.Trim(),
            TramiteModalidadEntradaCodes.MatriculaInicial,
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>Logging source-generated (CA1848) de la importación masiva. No registra PII del archivo.</summary>
internal static partial class BulkImportLog
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Error inesperado importando la fila {RowNumber}; se marca como fallida y el lote continúa.")]
    public static partial void RowFailed(ILogger logger, Exception ex, int rowNumber);
}
