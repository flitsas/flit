using System.Text.Json;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Procesa un lote de carga masiva fila a fila (HU #12523).
///
/// <para><b>Secuencial a propósito, no por simplicidad.</b> Cada fila dispara consultas al proveedor
/// externo (RUNT vía Kyverum/Verifik) y ya se sabe —HU #12309, Feature #12276— que consultas
/// simultáneas de la misma placa se colapsan entre sí y el proveedor devuelve 409. Paralelizar aquí
/// reintroduciría ese fallo sobre un lote entero.</para>
///
/// <para><b>Una fila mala no mata el lote.</b> Cada fila se resuelve en su propio try/catch y su
/// propio resultado persistido; el bucle nunca se corta a medias. Las tres salidas posibles salen
/// de lo que pidió el PO:</para>
/// <list type="bullet">
/// <item><b>Vehículo que falla ⇒ no se crea el trámite</b> (<c>not_created</c>): sin vehículo no hay
/// trámite que valga, y el motivo típico es un dato mal escrito (placa o VIN).</item>
/// <item><b>Vehículo bien pero actor que falla ⇒ el trámite SÍ se crea</b>
/// (<c>created_pending</c>), marcado para que el usuario lo retome desde el wizard en el paso donde
/// quedó. Tirar el trámite obligaría a repetir la consulta que ya salió bien.</item>
/// <item><b>Todo bien ⇒ <c>created</c>.</b></item>
/// </list>
/// </summary>
public sealed class BulkTramitesBatchProcessor(
    IBulkTramitesBatchRepository repository,
    IBulkTramitesWizardGateway gateway,
    TimeProvider clock)
{
    public async Task ProcessAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await repository.GetByIdAsync(batchId, ct).ConfigureAwait(false);
        if (batch is null || batch.Status != BulkTramitesBatchStatus.Queued)
        {
            return;
        }

        var tipo = BulkTramitesTemplateTypeParser.Parse(batch.TemplateType);
        if (tipo is null)
        {
            // Un tipo que no se reconoce no se puede procesar ni reintentar: se cierra el lote para
            // que no quede reclamándose para siempre, con el motivo visible en cada fila.
            await CloseUnprocessableAsync(batch, ct).ConfigureAwait(false);
            return;
        }

        batch.Status = BulkTramitesBatchStatus.Processing;
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var row in batch.Rows.Where(r => r.StructuralErrorCode is null && r.Outcome is null)
                     .OrderBy(r => r.RowNumber))
        {
            await ProcessRowAsync(batch, tipo.Value, row, ct).ConfigureAwait(false);

            // Se persiste fila a fila y no al final: así el resumen de /tramites (HU #12524) avanza
            // mientras el lote corre, y una caída del proceso no pierde lo ya consultado.
            await repository.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        batch.Status = BulkTramitesBatchStatus.Completed;
        batch.CompletedAt = clock.GetUtcNow();
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessRowAsync(
        BulkTramitesBatch batch, BulkTramitesTemplateType tipo, BulkTramitesBatchRow row, CancellationToken ct)
    {
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.ValuesJson)
                ?? new Dictionary<string, string?>();

            var context = BulkTramitesRowMapper.Map(tipo, batch.TenantId, batch.CreatedByUserId, values);

            var (previewToken, previewError) = await gateway
                .PreviewVehicleAsync(context, ct).ConfigureAwait(false);
            if (previewError is not null)
            {
                Resolve(row, BulkTramitesRowOutcome.NotCreated, previewError, null);
                return;
            }

            var (instanceId, createError) = await gateway
                .CreateTramiteAsync(context, previewToken, ct).ConfigureAwait(false);

            if (instanceId is null)
            {
                Resolve(row, BulkTramitesRowOutcome.NotCreated, createError ?? "create_failed", null);
                return;
            }

            if (createError is not null)
            {
                // El trámite existe aunque la creación reportara error (p. ej. el preflight
                // autoritativo falló después de persistir): es exactamente el caso de «retomar».
                Resolve(row, BulkTramitesRowOutcome.CreatedPending, createError, instanceId);
                return;
            }

            if (context.Actors.Count == 0)
            {
                Resolve(row, BulkTramitesRowOutcome.CreatedPending, "sin_actores_en_la_fila", instanceId);
                return;
            }

            var actorsError = await gateway
                .SaveActorsAsync(instanceId.Value, batch.TenantId, context.Actors, ct)
                .ConfigureAwait(false);

            Resolve(
                row,
                actorsError is null ? BulkTramitesRowOutcome.Created : BulkTramitesRowOutcome.CreatedPending,
                actorsError,
                instanceId);
        }
#pragma warning disable CA1031 // Un fallo inesperado de UNA fila no puede tumbar el lote entero.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Resolve(row, BulkTramitesRowOutcome.NotCreated, "error_inesperado", null);
        }
    }

    private void Resolve(BulkTramitesBatchRow row, string outcome, string? reason, Guid? instanceId)
    {
        row.Outcome = outcome;
        row.OutcomeReason = reason;
        row.ProcedureInstanceId = instanceId;
        row.ProcessedAt = clock.GetUtcNow();
    }

    private async Task CloseUnprocessableAsync(BulkTramitesBatch batch, CancellationToken ct)
    {
        foreach (var row in batch.Rows.Where(r => r.Outcome is null))
        {
            Resolve(row, BulkTramitesRowOutcome.NotCreated, "tipo_de_plantilla_no_soportado", null);
        }

        batch.Status = BulkTramitesBatchStatus.Completed;
        batch.CompletedAt = clock.GetUtcNow();
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
