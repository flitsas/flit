using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.BulkTramites;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;

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
        // Processing también entra: es un lote reclamado tras quedar huérfano (ver el repositorio);
        // solo se retoman las filas sin resultado, así que reprocesar no duplica trámites.
        if (batch is null || batch.Status is not (BulkTramitesBatchStatus.Queued or BulkTramitesBatchStatus.Processing))
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
                var (resuelto, lookupError) = EsJuridica(actor)
                    ? await ResolveEmpresaAsync(instanceId.Value, batch.TenantId, actor, ct).ConfigureAwait(false)
                    : await ResolvePersonaAsync(instanceId.Value, batch.TenantId, actor, ct).ConfigureAwait(false);

                if (lookupError is not null)
                {
                    Resolve(
                        row,
                        BulkTramitesRowOutcome.CreatedPending,
                        $"{lookupError}:{actor.TipoDocumento} {actor.NumeroDocumento}",
                        instanceId);
                    return;
                }

                actores.Add(resuelto!);
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

    /// <summary>
    /// Persona natural: nombre del RUNT (consulta de conductor), con un reintento si el proveedor se cae.
    /// </summary>
    private async Task<(ActorInput? Actor, string? Error)> ResolvePersonaAsync(
        Guid instanceId, Guid tenantId, ActorInput actor, CancellationToken ct)
    {
        var (fullName, lookupError) = await gateway
            .LookupPersonAsync(instanceId, tenantId, actor.TipoDocumento, actor.NumeroDocumento, ct)
            .ConfigureAwait(false);
        if (lookupError == BulkTramitesWizardGateway.ConsultaConductorFallida)
        {
            await Task.Delay(_providerRetryDelay, clock, ct).ConfigureAwait(false);
            (fullName, lookupError) = await gateway
                .LookupPersonAsync(instanceId, tenantId, actor.TipoDocumento, actor.NumeroDocumento, ct)
                .ConfigureAwait(false);
        }

        return lookupError is not null
            ? (null, lookupError)
            : (actor with { NombreCompleto = fullName! }, null);
    }

    /// <summary>
    /// Persona jurídica (HU #12538): razón social de RUES y representante legal del directorio de la
    /// empresa, igual que la precarga por NIT del wizard. El representante NO se inventa: sin uno
    /// registrado la fila queda por retomar, porque es él quien valida la identidad y firma. El
    /// mecanismo de firma se deja sin elegir a propósito: así aplica la precedencia del baúl que ya
    /// resuelve el guardado del wizard (HU #11031), y si el representante tiene firma vigente no se
    /// manda correo de validación.
    /// </summary>
    private async Task<(ActorInput? Actor, string? Error)> ResolveEmpresaAsync(
        Guid instanceId, Guid tenantId, ActorInput actor, CancellationToken ct)
    {
        var (empresa, lookupError) = await gateway
            .LookupCompanyAsync(instanceId, tenantId, actor.NumeroDocumento, ct)
            .ConfigureAwait(false);
        if (lookupError == BulkTramitesWizardGateway.ConsultaEmpresaFallida)
        {
            await Task.Delay(_providerRetryDelay, clock, ct).ConfigureAwait(false);
            (empresa, lookupError) = await gateway
                .LookupCompanyAsync(instanceId, tenantId, actor.NumeroDocumento, ct)
                .ConfigureAwait(false);
        }

        if (lookupError is not null)
        {
            return (null, lookupError);
        }

        var directorio = empresa!.Directorio;
        if (directorio is null || directorio.Representantes.Count == 0)
        {
            return (null, PersonaJuridicaSinRepresentanteRegistrado);
        }

        var elegido = actor.RepresentanteLegal?.NumeroDocumento;
        var representante = elegido is null
            ? directorio.Representantes[0]
            : directorio.Representantes.FirstOrDefault(r => MismoDocumento(r.NumeroDocumento, elegido));
        if (representante is null)
        {
            return (null, RepresentanteNoRegistrado);
        }

        // Lo escrito en la fila manda; el directorio rellena lo que falte (mismo orden que aplica
        // el wizard cuando precarga el contacto de la compañía).
        return (actor with
        {
            NombreCompleto = RazonSocialCorta(empresa.RazonSocial),
            PersonType = ActorPersonTypes.Juridical,
            Email = string.IsNullOrWhiteSpace(actor.Email) ? directorio.Email?.Trim() ?? string.Empty : actor.Email,
            Telefono = actor.Telefono ?? directorio.Telefono,
            Ciudad = actor.Ciudad ?? directorio.Ciudad,
            Direccion = actor.Direccion ?? directorio.Direccion,
            RepresentanteLegal = new ActorRepresentanteLegal(
                representante.TipoDocumento,
                representante.NumeroDocumento,
                representante.NombreCompleto,
                representante.Email,
                representante.Telefono,
                MecanismoFirma: null),
        }, null);
    }

    /// <summary>La empresa existe en RUES pero el tenant no tiene ningún representante suyo en el directorio.</summary>
    public const string PersonaJuridicaSinRepresentanteRegistrado = "persona_juridica_sin_representante_registrado";

    /// <summary>La fila eligió una cédula de representante que no es de ninguno de los registrados para ese NIT.</summary>
    public const string RepresentanteNoRegistrado = "representante_no_registrado";

    private static bool EsJuridica(ActorInput actor) =>
        ActorPersonTypes.IsJuridical(ActorPersonTypes.ResolveForDocument(actor.TipoDocumento, actor.PersonType));

    /// <summary>Empata cédulas aunque vengan con puntos o ceros a la izquierda, como hace el wizard.</summary>
    private static bool MismoDocumento(string a, string b) => SoloDigitos(a) == SoloDigitos(b);

    private static string SoloDigitos(string valor) =>
        new string(valor.Where(char.IsDigit).ToArray()).TrimStart('0');

    /// <summary>
    /// RUES a veces entrega la razón social con cláusulas societarias en el mismo campo, separadas
    /// por coma («BANCOLOMBIA S.A., ADEMÁS PODRÁ GIRAR…»). El wizard se queda con el tramo anterior
    /// a la coma; aquí igual, para que el actor se llame lo mismo por las dos vías.
    /// </summary>
    private static string RazonSocialCorta(string razonSocial)
    {
        var texto = razonSocial.Trim();
        var coma = texto.IndexOf(',', StringComparison.Ordinal);
        if (coma < 0)
        {
            return texto;
        }

        var cabeza = texto[..coma].Trim();
        return cabeza.Length > 0 ? cabeza : texto;
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
