using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
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
/// quedó. Tirar el trámite obligaría a repetir la consulta que ya salió bien. «Actor que falla»
/// incluye la consulta de persona al RUNT: el nombre del actor NO viene en el Excel, sale de esa
/// consulta, así que sin ella no hay nada que guardar y el paso de actores queda para el wizard.
/// El motivo lleva el documento que falló (<c>codigo:TIPO NUMERO</c>) porque una fila de traspaso
/// puede traer hasta 8 personas y «conductor no encontrado» a secas no dice cuál.</item>
/// <item><b>Todo bien ⇒ <c>created</c>.</b></item>
/// </list>
/// </summary>
public sealed class BulkTramitesBatchProcessor(
    IBulkTramitesBatchRepository repository,
    IBulkTramitesWizardGateway gateway,
    TimeProvider clock,
    TimeSpan? providerRetryDelay = null)
{
    /// <summary>
    /// Espera antes del ÚNICO reintento cuando el proveedor se cae. Probando en vivo con Samuel
    /// Cardenas, Kyverum devolvía 502/500 transitorios casi siempre en la consulta que seguía
    /// inmediatamente a otra (vehículo → persona, persona → persona) y respondía bien segundos
    /// después: sin esto, filas con datos correctos quedaban «por retomar» por un hueco del
    /// proveedor. Un solo reintento: si falla dos veces seguidas no es un hueco, es una caída.
    /// </summary>
    public static readonly TimeSpan DefaultProviderRetryDelay = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _providerRetryDelay = providerRetryDelay ?? DefaultProviderRetryDelay;

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
            if (previewError == BulkTramitesVehicleGate.ConsultaVehiculoFallida)
            {
                await Task.Delay(_providerRetryDelay, clock, ct).ConfigureAwait(false);
                (previewToken, previewError) = await gateway
                    .PreviewVehicleAsync(context, ct).ConfigureAwait(false);
            }

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

            // Consulta de persona ANTES de guardar, y todas o ninguna: el guardado del wizard es un
            // upsert del conjunto completo (con reglas entre actores, p. ej. los porcentajes deben
            // sumar 100 por lado), así que guardar «los que sí se encontraron» no pasaría igual.
            var actores = new List<ActorInput>(context.Actors.Count);
            foreach (var actor in context.Actors)
            {
                var (fullName, lookupError) = await gateway
                    .LookupPersonAsync(instanceId.Value, batch.TenantId, actor.TipoDocumento, actor.NumeroDocumento, ct)
                    .ConfigureAwait(false);
                if (lookupError == BulkTramitesWizardGateway.ConsultaConductorFallida)
                {
                    await Task.Delay(_providerRetryDelay, clock, ct).ConfigureAwait(false);
                    (fullName, lookupError) = await gateway
                        .LookupPersonAsync(instanceId.Value, batch.TenantId, actor.TipoDocumento, actor.NumeroDocumento, ct)
                        .ConfigureAwait(false);
                }

                if (lookupError is not null)
                {
                    Resolve(
                        row,
                        BulkTramitesRowOutcome.CreatedPending,
                        $"{lookupError}:{actor.TipoDocumento} {actor.NumeroDocumento}",
                        instanceId);
                    return;
                }

                actores.Add(actor with { NombreCompleto = fullName! });
            }

            var actorsError = await gateway
                .SaveActorsAsync(instanceId.Value, batch.TenantId, actores, ct)
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
