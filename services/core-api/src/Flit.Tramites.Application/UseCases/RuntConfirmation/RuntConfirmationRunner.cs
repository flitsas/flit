using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>Parámetros de operación de la corrida (appsettings <c>RuntConfirmation:*</c>).</summary>
public sealed class RuntConfirmationRunnerOptions
{
    /// <summary>Llamadas simultáneas al proveedor (HU #12309 AC10, default 4).</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Una corrida con <c>finished_at</c> NULL más vieja que esto se considera muerta y no bloquea la siguiente.</summary>
    public int StaleRunAfterHours { get; set; } = 6;

    /// <summary>Tope de trámites por corrida como protección del proceso (no es un campo de pantalla: el PO lo descartó).</summary>
    public int MaxCandidatesPerRun { get; set; } = 5000;
}

public sealed record RuntConfirmationRunRequest(
    string Trigger,
    IReadOnlyList<Guid>? OnlyInstanceIds = null,
    Guid? RequestedBy = null);

/// <summary>
/// La corrida (HU #12309): toma el universo, consulta al proveedor configurado según la familia,
/// guarda el crudo ANTES de evaluar, aplica el motor y deja un intento por trámite y una fila por
/// corrida. Nunca toca el <c>status</c> del trámite. Un error del proveedor deja un intento
/// <c>error</c> que no consume número ni marca nada: v1 confirmaba en el <c>catch</c> y una noche
/// de caída del proveedor marcaba toda la tanda como aprobada en el RUNT.
/// </summary>
public sealed class RuntConfirmationRunner(
    IRuntConfirmationSettingsRepository settingsRepository,
    IRuntConfirmationStore store,
    IRuntVehicleRawClient client,
    RuntConfirmationRunnerOptions options,
    ILogger<RuntConfirmationRunner> logger)
{
    public async Task<RuntConfirmationRun> RunAsync(RuntConfirmationRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = await settingsRepository.GetAsync(ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var scheduled = request.Trigger == RuntConfirmationRunTriggers.Scheduled;

        var run = new RuntConfirmationRun
        {
            Id = Guid.CreateVersion7(),
            Trigger = request.Trigger,
            StartedAt = now,
            ProviderKey = settings.ProviderKey,
        };

        // El interruptor gobierna la corrida automática. Un «Consultar ahora» es un acto explícito
        // de un usuario con permiso y no depende de él.
        if (scheduled && !settings.Enabled)
            return await SkipAsync(run, RuntConfirmationSkipReasons.Disabled, ct).ConfigureAwait(false);

        if (scheduled && await store.IsRunInProgressAsync(TimeSpan.FromHours(options.StaleRunAfterHours), ct).ConfigureAwait(false))
            return await SkipAsync(run, RuntConfirmationSkipReasons.AlreadyRunning, ct).ConfigureAwait(false);

        run = await store.StartRunAsync(run, ct).ConfigureAwait(false);

        var counters = new Counters();
        try
        {
            var candidates = await LoadCandidatesAsync(request, settings, now, ct).ConfigureAwait(false);
            RunnerLog.Started(logger, run.Id, request.Trigger, settings.ProviderKey, candidates.Count);

            // Paralelo entre vehículos, secuencial dentro del mismo: el proveedor colapsa consultas
            // simultáneas de una misma placa (ver ProcessCandidateAsync), así que dos trámites del mismo
            // vehículo en la misma corrida van uno detrás del otro.
            using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
            var tasks = candidates
                .GroupBy(c => c.Plate?.Trim().ToUpperInvariant() is { Length: > 0 } plate ? plate : c.InstanceId.ToString())
                .Select(async group =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        foreach (var candidate in group)
                            await ProcessCandidateAsync(run, settings, candidate, request.RequestedBy, counters, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        gate.Release();
                    }
                });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.ErrorMessage = "Corrida interrumpida (cancelación del proceso).";
        }
        catch (Exception ex)
        {
            run.ErrorMessage = Truncate($"{ex.GetType().Name}: {ex.Message}", 1000);
            RunnerLog.RunFailed(logger, run.Id, ex);
        }

        counters.CopyTo(run);
        run.FinishedAt = DateTimeOffset.UtcNow;
        // Se cierra aunque el token esté cancelado: una fila sin finished_at es indistinguible de un
        // worker muerto.
        await store.FinishRunAsync(run, CancellationToken.None).ConfigureAwait(false);
        RunnerLog.Finished(logger, run.Id, run.Consulted, run.Confirmed, run.Pending, run.Discrepancies, run.Unverifiable, run.Errors, run.ProviderCalls);
        return run;
    }

    private async Task<RuntConfirmationRun> SkipAsync(RuntConfirmationRun run, string reason, CancellationToken ct)
    {
        run.SkippedReason = reason;
        run.FinishedAt = run.StartedAt;
        run = await store.StartRunAsync(run, ct).ConfigureAwait(false);
        RunnerLog.Skipped(logger, run.Id, reason);
        return run;
    }

    private async Task<IReadOnlyList<RuntConfirmationCandidate>> LoadCandidatesAsync(
        RuntConfirmationRunRequest request, RuntConfirmationSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        if (request.OnlyInstanceIds is { Count: > 0 } ids)
        {
            var list = new List<RuntConfirmationCandidate>(ids.Count);
            foreach (var id in ids)
            {
                var c = await store.GetCandidateAsync(id, ct).ConfigureAwait(false);
                if (c is not null)
                    list.Add(c);
            }
            return list;
        }

        return await store.ListUniverseAsync(
            new RuntConfirmationUniverseFilter(settings.MaxAttempts, settings.GraceDays, now, options.MaxCandidatesPerRun), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Un trámite: consulta según familia, crudo, motor, intento. Aislado: su fallo no tumba la corrida.</summary>
    private async Task ProcessCandidateAsync(
        RuntConfirmationRun run,
        RuntConfirmationSettings settings,
        RuntConfirmationCandidate candidate,
        Guid? requestedBy,
        Counters counters,
        CancellationToken ct)
    {
        var queriedAt = DateTimeOffset.UtcNow;
        var attemptNo = candidate.RuntAttempts + 1;
        counters.Consulted();

        try
        {
            var plan = QueryPlan.For(candidate);
            if (plan.Unverifiable is not null)
            {
                var d = RuntConfirmationRules.Evaluate(new RuntConfirmationInput(
                    candidate.ProcedureTypeCode, candidate.Family, RuntConfirmationEvaluator.CutoffDay(candidate.CutoffAt),
                    null, null, null, candidate.Plate, candidate.TransitOfficeName)) with
                {
                    Verdict = RuntConfirmationVerdict.Unverifiable,
                    Reason = plan.Unverifiable,
                };
                await RecordAsync(run, settings, candidate, attemptNo, queriedAt, plan.Kind, d, null, null, requestedBy, counters, ct).ConfigureAwait(false);
                return;
            }

            // Consultas al proveedor. En traspaso las dos van EN SECUENCIA, vendedor primero, nunca en
            // paralelo: Kyverum colapsa dos peticiones simultáneas de la misma placa en una sola
            // resolución (la que llega primero manda, sin importar el documento) y Verifik responde
            // 409 a la segunda. Visto en dev el 2026-09-10 con JNH38H: los dos crudos salían byte a
            // byte iguales y el veredicto se daba con datos falsos.
            var seller = plan.Seller is null ? null : await client.ConsultAsync(settings.ProviderKey, plan.Seller, ct).ConfigureAwait(false);
            var primary = await client.ConsultAsync(settings.ProviderKey, plan.Primary!, ct).ConfigureAwait(false);
            counters.ProviderCalls(plan.Seller is null ? 1 : 2);

            // AC4 — el crudo se guarda ANTES de evaluar, aunque el veredicto vaya a ser error.
            var primaryId = await SaveAsync(candidate, settings.ProviderKey, plan.Primary!, primary, queriedAt, ct).ConfigureAwait(false);
            var sellerId = plan.Seller is null ? null : await SaveAsync(candidate, settings.ProviderKey, plan.Seller, seller!, queriedAt, ct).ConfigureAwait(false);

            if (primary.Outcome == RuntRawOutcome.Error || seller?.Outcome == RuntRawOutcome.Error)
            {
                var message = primary.Outcome == RuntRawOutcome.Error ? primary.Message : seller!.Message;
                var d = new RuntConfirmationDecision(
                    RuntConfirmationVerdict.Error,
                    $"El proveedor no respondió ({message ?? "error"}); no cuenta como intento y se reintenta en la siguiente corrida.",
                    RuntConfirmationRules.Version);
                await RecordAsync(run, settings, candidate, attemptNo, queriedAt, plan.Kind, d, primaryId, sellerId, requestedBy, counters, ct).ConfigureAwait(false);
                return;
            }

            // Colapso del proveedor: dos documentos distintos no pueden producir exactamente el mismo
            // cuerpo (Kyverum ecoa tipoDocPropietario; Verifik, documentNumber). Si pasa, es la caché por
            // placa del proveedor, no un dato del RUNT: error de proveedor, sin consumir intento.
            if (seller is not null && ProviderCollapsed(plan, primary, seller))
            {
                var d = new RuntConfirmationDecision(
                    RuntConfirmationVerdict.Error,
                    "El proveedor devolvió exactamente la misma respuesta para el documento del comprador y el del vendedor (caché por placa del proveedor); no se puede distinguir quién es el propietario; no cuenta como intento y se reintenta en la siguiente corrida.",
                    RuntConfirmationRules.Version);
                await RecordAsync(run, settings, candidate, attemptNo, queriedAt, plan.Kind, d, primaryId, sellerId, requestedBy, counters, ct).ConfigureAwait(false);
                return;
            }

            var baseline = await store.GetBaselinePayloadJsonAsync(candidate.InstanceId, candidate.CutoffAt, ct).ConfigureAwait(false);
            var decision = RuntConfirmationEvaluator.Evaluate(candidate, primary.RawJson, seller?.RawJson, baseline);

            // Segunda línea de matrícula: el historial no sirvió pero el RUNT ya reporta placa. Una llamada
            // más, solo aquí, con el documento del propietario del expediente. Su crudo va en el hueco del
            // vendedor (misma columna, otro significado: lo dice query_kind).
            if (decision.TiebreakPlate is { } placa && (candidate.Buyer ?? candidate.Owner) is { } propietario)
            {
                var tiebreakQuery = RuntRawQuery.ByPlate(placa, propietario);
                var tiebreak = await client.ConsultAsync(settings.ProviderKey, tiebreakQuery, ct).ConfigureAwait(false);
                counters.ProviderCalls(1);
                sellerId = await SaveAsync(candidate, settings.ProviderKey, tiebreakQuery, tiebreak, queriedAt, ct).ConfigureAwait(false);

                if (tiebreak.Outcome == RuntRawOutcome.Error)
                {
                    var d = new RuntConfirmationDecision(
                        RuntConfirmationVerdict.Error,
                        $"El proveedor no respondió la consulta de desempate por placa ({tiebreak.Message ?? "error"}); no cuenta como intento y se reintenta en la siguiente corrida.",
                        RuntConfirmationRules.Version);
                    await RecordAsync(run, settings, candidate, attemptNo, queriedAt, RuntConfirmationQueryKinds.VinPlate, d, primaryId, sellerId, requestedBy, counters, ct).ConfigureAwait(false);
                    return;
                }

                // Un «no encontrado» es un dato para el motor (el propietario no responde), no una ausencia.
                var tiebreakJson = tiebreak.RawJson ?? RuntVehicleSnapshotParser.NotFoundPayload(settings.ProviderKey, null, tiebreak.Message);
                decision = RuntConfirmationEvaluator.Evaluate(candidate, primary.RawJson, null, baseline, tiebreakJson);
                await RecordAsync(run, settings, candidate, attemptNo, queriedAt, RuntConfirmationQueryKinds.VinPlate, decision, primaryId, sellerId, requestedBy, counters, ct).ConfigureAwait(false);
                return;
            }

            await RecordAsync(run, settings, candidate, attemptNo, queriedAt, plan.Kind, decision, primaryId, sellerId, requestedBy, counters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fallo propio (BD, bug): queda como error del trámite, sin consumir intento, y la corrida sigue.
            counters.Add(RuntConfirmationVerdict.Error);
            RunnerLog.CandidateFailed(logger, run.Id, candidate.InstanceId, ex);
            try
            {
                // El motivo lo lee gente de operación: sin nombres de excepción ni mensajes de EF. El detalle
                // técnico ya quedó en el log con el id de la corrida y del trámite.
                var d = new RuntConfirmationDecision(
                    RuntConfirmationVerdict.Error,
                    $"FLIT no pudo terminar de procesar este trámite (fallo interno, no del proveedor); no cuenta como intento y se reintenta en la siguiente corrida. Detalle en el log del servidor, corrida {run.Id}.",
                    RuntConfirmationRules.Version);
                var attempt = BuildAttempt(run, candidate, attemptNo, queriedAt, RuntConfirmationQueryKinds.Plate, settings.ProviderKey, d, null, null, requestedBy, null);
                await store.RecordAttemptAsync(attempt, new RuntConfirmationInstanceUpdate(false, null, null, false), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception inner)
            {
                RunnerLog.CandidateFailed(logger, run.Id, candidate.InstanceId, inner);
            }
        }
    }

    /// <summary>
    /// Dos consultas con documentos distintos y el mismo cuerpo crudo, byte a byte. Solo aplica cuando
    /// ambas devolvieron algo (un «no encontrado» sintetizado por FLIT es igual para las dos por
    /// construcción y no dice nada del proveedor).
    /// </summary>
    internal static bool ProviderCollapsed(QueryPlan plan, RuntRawQueryResult primary, RuntRawQueryResult seller) =>
        plan.Primary?.Document is { } a && plan.Seller?.Document is { } b
        && !string.Equals(a.Number, b.Number, StringComparison.Ordinal)
        && primary.Outcome == RuntRawOutcome.Found && seller.Outcome == RuntRawOutcome.Found
        && primary.RawJson is { Length: > 0 } && string.Equals(primary.RawJson, seller.RawJson, StringComparison.Ordinal);

    private async Task<Guid?> SaveAsync(
        RuntConfirmationCandidate candidate, string providerKey, RuntRawQuery query, RuntRawQueryResult result, DateTimeOffset queriedAt, CancellationToken ct)
    {
        var json = result.RawJson;
        if (json is null && result.Outcome == RuntRawOutcome.NotFound)
            json = RuntVehicleSnapshotParser.NotFoundPayload(providerKey, null, result.Message);
        if (json is null)
            return null;

        return await store.SaveRawPayloadAsync(candidate.TenantId, candidate.InstanceId, providerKey, query.SubjectKey, json, queriedAt, ct).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        RuntConfirmationRun run,
        RuntConfirmationSettings settings,
        RuntConfirmationCandidate candidate,
        int attemptNo,
        DateTimeOffset queriedAt,
        string queryKind,
        RuntConfirmationDecision decision,
        Guid? primaryId,
        Guid? sellerId,
        Guid? requestedBy,
        Counters counters,
        CancellationToken ct)
    {
        var update = RuntConfirmationEvaluator.ApplyVerdict(
            decision, attemptNo, settings.DiscrepancyAfterRuns, settings.MaxAttempts, DateTimeOffset.UtcNow, out var flag);

        var attempt = BuildAttempt(run, candidate, attemptNo, queriedAt, queryKind, settings.ProviderKey, decision, primaryId, sellerId, requestedBy, flag);
        await store.RecordAttemptAsync(attempt, update, ct).ConfigureAwait(false);
        counters.Add(decision.Verdict);
    }

    private static RuntConfirmationAttempt BuildAttempt(
        RuntConfirmationRun run,
        RuntConfirmationCandidate candidate,
        int attemptNo,
        DateTimeOffset queriedAt,
        string queryKind,
        string providerKey,
        RuntConfirmationDecision decision,
        Guid? primaryId,
        Guid? sellerId,
        Guid? requestedBy,
        string? flag) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = candidate.TenantId,
            ProcedureInstanceId = candidate.InstanceId,
            RunId = run.Id,
            AttemptNo = attemptNo,
            QueriedAt = queriedAt,
            ProviderKey = providerKey,
            QueryKind = queryKind,
            Verdict = RuntConfirmationVerdictCodes.ToCode(decision.Verdict),
            ReasonText = Truncate(decision.Reason, 2000),
            RuleVersion = decision.RuleVersion,
            RawPayloadId = primaryId,
            SellerRawPayloadId = sellerId,
            RequestedBy = requestedBy,
            FlagApplied = flag,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Cómo se consulta cada familia (AC3). Sin los datos necesarios, el trámite es No verificable y sale del universo.</summary>
    internal sealed record QueryPlan(string Kind, RuntRawQuery? Primary, RuntRawQuery? Seller, string? Unverifiable)
    {
        public static QueryPlan For(RuntConfirmationCandidate c)
        {
            var plate = c.Plate?.Trim().ToUpperInvariant();
            var vin = c.Vin?.Trim().ToUpperInvariant();

            switch (c.Family)
            {
                case ProcedureFamily.Matriculas:
                    if (!string.IsNullOrWhiteSpace(vin))
                        return new(RuntConfirmationQueryKinds.Vin, RuntRawQuery.ByVin(vin), null, null);
                    if (!string.IsNullOrWhiteSpace(plate) && (c.Buyer ?? c.Owner) is { } doc)
                        return new(RuntConfirmationQueryKinds.Plate, RuntRawQuery.ByPlate(plate, doc), null, null);
                    return new(RuntConfirmationQueryKinds.Vin, null, null, "El expediente no tiene VIN ni placa con documento del propietario: no se puede consultar al RUNT.");

                case ProcedureFamily.Traspaso:
                    if (string.IsNullOrWhiteSpace(plate))
                        return new(RuntConfirmationQueryKinds.PlatePair, null, null, "El expediente no tiene placa: no se puede consultar al RUNT.");
                    if (c.Seller is null || c.Buyer is null)
                        return new(RuntConfirmationQueryKinds.PlatePair, null, null, "El expediente no tiene documento de vendedor y comprador: no se puede hacer el par de consultas.");
                    return new(RuntConfirmationQueryKinds.PlatePair, RuntRawQuery.ByPlate(plate, c.Buyer), RuntRawQuery.ByPlate(plate, c.Seller), null);

                default:
                    if (string.IsNullOrWhiteSpace(plate) || (c.Owner ?? c.Buyer ?? c.Seller) is not { } owner)
                        return new(RuntConfirmationQueryKinds.Plate, null, null, "El expediente no tiene placa con documento del propietario: no se puede consultar al RUNT.");
                    return new(RuntConfirmationQueryKinds.Plate, RuntRawQuery.ByPlate(plate, owner), null, null);
            }
        }
    }

    private sealed class Counters
    {
        private int _consulted, _confirmed, _pending, _discrepancies, _unverifiable, _errors, _calls;

        public void Consulted() => Interlocked.Increment(ref _consulted);
        public void ProviderCalls(int n) => Interlocked.Add(ref _calls, n);

        public void Add(RuntConfirmationVerdict v)
        {
            switch (v)
            {
                case RuntConfirmationVerdict.Confirmed: Interlocked.Increment(ref _confirmed); break;
                case RuntConfirmationVerdict.Pending: Interlocked.Increment(ref _pending); break;
                case RuntConfirmationVerdict.Discrepancy: Interlocked.Increment(ref _discrepancies); break;
                case RuntConfirmationVerdict.Unverifiable: Interlocked.Increment(ref _unverifiable); break;
                default: Interlocked.Increment(ref _errors); break;
            }
        }

        public void CopyTo(RuntConfirmationRun run)
        {
            run.Consulted = _consulted;
            run.Confirmed = _confirmed;
            run.Pending = _pending;
            run.Discrepancies = _discrepancies;
            run.Unverifiable = _unverifiable;
            run.Errors = _errors;
            run.ProviderCalls = _calls;
        }
    }
}

internal static partial class RunnerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmación RUNT: corrida {RunId} ({Trigger}) con {Provider}: {Count} trámites en el universo.")]
    public static partial void Started(ILogger logger, Guid runId, string trigger, string provider, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmación RUNT: corrida {RunId} saltada ({Reason}).")]
    public static partial void Skipped(ILogger logger, Guid runId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Confirmación RUNT: corrida {RunId} terminó. consultados={Consulted} confirmados={Confirmed} pendientes={Pending} discrepancias={Discrepancies} no_verificables={Unverifiable} errores={Errors} llamadas={Calls}.")]
    public static partial void Finished(ILogger logger, Guid runId, int consulted, int confirmed, int pending, int discrepancies, int unverifiable, int errors, int calls);

    [LoggerMessage(Level = LogLevel.Error, Message = "Confirmación RUNT: la corrida {RunId} abortó.")]
    public static partial void RunFailed(ILogger logger, Guid runId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Confirmación RUNT: fallo procesando el trámite {InstanceId} en la corrida {RunId}.")]
    public static partial void CandidateFailed(ILogger logger, Guid runId, Guid instanceId, Exception ex);
}
