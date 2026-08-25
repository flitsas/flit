using System.Collections.Concurrent;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Domain.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// CF-02 (HU #10879, AC3) — consulta del vehículo del PASO 1 cuando el trámite todavía NO existe.
/// Lleva el identificador capturado por el operador (VIN en matrícula inicial, placa + documento del
/// propietario en traspaso) y NO referencia ninguna instancia.
/// </summary>
public sealed record PreflightPreviewRequest(
    Guid TenantId,
    string Modalidad,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    /// <summary>
    /// HU #11199 — secretaría elegida en el primer paso. OBLIGATORIA en matrícula inicial: sin ella
    /// no se consulta el VIN. En traspaso llega nula (el organismo lo impone el RUNT, HU #11200).
    /// </summary>
    Guid? TransitOfficeId = null,
    /// <summary>
    /// ADR-0050 — <c>code</c> del tipo elegido. Decide qué identificador exige la consulta; sin él
    /// se cae a la familia heredada, que solo distingue matrícula de traspaso.
    /// </summary>
    string? ProcedureTypeCode = null);

/// <summary>Atributo del vehículo hidratado por la consulta, en la forma que ya consume el wizard.</summary>
public sealed record PreflightPreviewFieldDto(string FieldKey, string? ValueText, string? ValueJson);

/// <summary>
/// Semáforo del paso 1 sin trámite creado. <see cref="PreviewToken"/> identifica la consulta guardada
/// en el servidor: al avanzar al paso 2 se envía de vuelta para crear el trámite REUSANDO esta consulta,
/// sin volver a llamar al proveedor externo (AC5).
/// </summary>
public sealed record PreflightPreviewDto(
    string PreviewToken,
    string Overall,
    IReadOnlyList<PreflightCheckDto> Checks,
    string? Provider,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PreflightPreviewFieldDto> VehicleFields);

/// <summary>
/// Consulta de vehículo ya resuelta, lista para inyectarse en <see cref="RunPreflightHandler"/> cuando
/// la instancia ya existe. Solo cubre el tramo del VEHÍCULO: el resto de checks (comparendos, VIN con
/// matrícula previa, duplicidad) se recalcula siempre contra la instancia real.
/// </summary>
public sealed record PreflightVehicleSnapshot(
    IReadOnlyList<PreflightCheckDto> Checks,
    IReadOnlyList<HydratedField> HydratedFields,
    IReadOnlyList<string> Providers);

/// <summary>
/// Custodia server-side de las consultas del paso 1 mientras el trámite aún no existe. El payload
/// NUNCA viaja por el cliente (solo el token), así que la creación del trámite no confía en datos
/// suministrados por el navegador.
/// </summary>
public interface IPreflightPreviewStore
{
    /// <summary>Guarda la consulta y devuelve el token con el que se recupera.</summary>
    string Save(Guid tenantId, PreflightVehicleSnapshot snapshot);

    /// <summary>
    /// Consume (one-shot) la consulta del token. <c>null</c> si no existe, expiró o pertenece a otro
    /// tenant: el llamador degrada a consulta fresca, nunca falla por esto.
    /// </summary>
    PreflightVehicleSnapshot? TryTake(Guid tenantId, string? token);
}

/// <summary>
/// Implementación en memoria del proceso, con expiración por TTL. Suficiente porque la ventana de vida
/// es la de un paso del wizard (minutos) y la ausencia del token degrada a consulta fresca — en un
/// despliegue multi-instancia el peor caso es repetir la consulta al RUNT, nunca un dato incorrecto.
/// </summary>
public sealed class InMemoryPreflightPreviewStore : IPreflightPreviewStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed record Entry(Guid TenantId, PreflightVehicleSnapshot Snapshot, DateTimeOffset ExpiresAt);

    public string Save(Guid tenantId, PreflightVehicleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var now = DateTimeOffset.UtcNow;
        PurgeExpired(now);

        var token = Guid.NewGuid().ToString("N");
        _entries[token] = new Entry(tenantId, snapshot, now.Add(Ttl));
        return token;
    }

    public PreflightVehicleSnapshot? TryTake(Guid tenantId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_entries.TryRemove(token, out var entry))
            return null;

        if (entry.TenantId != tenantId || DateTimeOffset.UtcNow >= entry.ExpiresAt)
            return null;

        return entry.Snapshot;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            if (now >= entry.ExpiresAt)
                _entries.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// CF-02 (HU #10879, AC3/AC4) — corre la consulta del vehículo y las precondiciones de entrada SIN
/// crear ni tocar ninguna instancia, para que el paso 1 del wizard funcione antes de que el trámite
/// exista. Reusa las MISMAS reglas que <see cref="RunPreflightHandler"/> (política de bloqueo por
/// compañía/OT, endurecimiento de <c>estado_vehiculo</c> en matrícula, duplicidad por familia y
/// precondición registral) para que el semáforo del paso 1 no diverja del que se persiste al crear.
///
/// <para>Lo que NO hace, por no haber instancia: hidratar field_values, fijar el OT desde el RUNT y
/// persistir el snapshot. Todo eso ocurre en <see cref="RunPreflightHandler"/> al crear el trámite
/// (AC5), reusando la consulta guardada en <see cref="IPreflightPreviewStore"/>.</para>
/// </summary>
public sealed class RunPreflightPreviewHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry,
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider,
    IConsultationRestrictionPolicy restrictionPolicy,
    IPreflightPreviewStore previewStore,
    ITransitOfficeResolver transitOfficeResolver,
    IConsultationBlockingPolicy? blockingPolicy = null,
    TramiteValidationPolicy? validationPolicy = null,
    IOtOperabilityGate? otOperability = null,
    IProcedureTypeRepository? typeRepo = null)
{
    private readonly IConsultationBlockingPolicy _blockingPolicy =
        blockingPolicy ?? NullConsultationBlockingPolicy.Instance;

    // HU #11200 — el gate del paso 1 comprueba lo MISMO que el de radicación: grant vigente (resolver)
    // + organismo operativo en la plataforma (esta compuerta). Si comprobara menos, el paso 1 dejaría
    // pasar trámites que la radicación rechaza, que es justamente lo que la HU viene a evitar. Sin
    // inyectar ⇒ permisivo, para no bloquear a los tests que no ejercitan la operabilidad.
    private readonly IOtOperabilityGate _otOperability = otOperability ?? NullOtOperabilityGate.Instance;

    // HU #10970 — mismos modos por ambiente que RunPreflightHandler: el paso 1 del wizard no puede
    // divergir del preflight que se persiste al crear el trámite. Sin inyectar ⇒ bloqueo duro.
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    public async Task<(PreflightPreviewDto? Result, string? Error, Guid? ExistingProcedureInstanceId, VehicleStateBlock? VehicleState)> HandleAsync(
        PreflightPreviewRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ADR-0050 — el tipo elegido manda sobre la familia heredada.
        ProcedureType? procedureType = null;
        if (typeRepo is not null && !string.IsNullOrWhiteSpace(request.ProcedureTypeCode))
        {
            procedureType = await typeRepo
                .GetByCodePublishedAsync(request.ProcedureTypeCode!.Trim(), ct)
                .ConfigureAwait(false);
            if (procedureType is null)
                return (null, "procedure_type_not_found", null, null);
        }

        var modalidad = procedureType is not null
            ? ProcedureFamilyCodes.FromCodeOrOtros(procedureType.Family)
            : ProcedureFamilyCodes.FromCodeOrLegacyModalidad(request.Modalidad);
        if (modalidad is null)
            return (null, "modalidad_not_available", null, null);

        // Qué identificador pide la consulta lo declara el tipo (`entryMode`): un trámite de la
        // familia OTROS entra por placa, y por familia habría pedido un VIN que el vehículo ya
        // matriculado no es lo que lo identifica.
        var esMatricula = procedureType is not null
            ? string.Equals(
                ProcedureTypeGateProfile.FromJson(procedureType.GateProfile).EntryMode,
                "VIN",
                StringComparison.OrdinalIgnoreCase)
            : modalidad == ProcedureFamily.Matriculas;
        var vin = Trim(request.Vin);
        var plate = Trim(request.Plate);

        // El identificador de la familia es obligatorio: sin él no hay nada que consultar (el
        // frontend ya lo valida, pero el contrato no puede depender de eso).
        if (esMatricula ? vin is null : plate is null)
            return (null, "identificador_requerido", null, null);

        // HU #11199 (AC2/AC5) — en MATRÍCULA INICIAL la secretaría es requisito para consultar: sin ella
        // no se gasta la consulta al RUNT. Se confirma contra los grants y el catálogo (AC3) porque la
        // lista pudo cargarse antes de que un administrador desactivara el organismo o revocara el grant.
        // En traspaso no aplica: allí el organismo lo impone el RUNT y se valida después (HU #11200).
        // El organismo es OPCIONAL en la consulta: el paso 1 pregunta dónde se radica DESPUÉS de
        // identificar el vehículo, y exigirlo aquí obligaba a elegirlo a ciegas. Cuando llega se
        // valida igual que antes (grants + catálogo + operabilidad, AC3); cuando no, las políticas
        // de abajo caen a los valores del tenant — exactamente lo que ya hacía traspaso, que nunca
        // trae organismo en este punto.
        //
        // El requisito NO desaparece: `CreateFromConsultaCommand` sigue cortando con
        // `transit_office_required` al crear el trámite, que es cuando el organismo tiene que
        // quedar guardado. Aquí solo se consulta el RUNT y no se persiste nada.
        ResolvedTransitOffice? secretaria = null;
        if (esMatricula && request.TransitOfficeId is { } elegido && elegido != Guid.Empty)
        {
            secretaria = await transitOfficeResolver
                .ResolveEnabledByIdAsync(request.TenantId, elegido, ct)
                .ConfigureAwait(false);
            if (secretaria is null
                || !await _otOperability.IsOperableAsync(secretaria.Id, ct).ConfigureAwait(false))
                return (null, TransitOfficeSelectionPolicy.UnavailableErrorCode, null, null);
        }

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (Trim(request.OwnerDocumentType) is { } docType) fieldValues["owner_document_type"] = docType;
        if (Trim(request.OwnerDocumentNumber) is { } docNumber) fieldValues["owner_document_number"] = docNumber;

        // CF-01 PRIMERO, antes de gastar la consulta externa: la duplicidad se decide solo con la llave
        // de la familia (VIN o placa), que ya tenemos. Consultar el RUNT para luego descartar el
        // resultado por duplicidad quemaba una consulta de pago en cada intento y retrasaba el aviso.
        // HU #10970 — este atajo solo tiene sentido en modo block (es el único que descarta la corrida).
        // En warn/off la consulta externa se hace igual, así que la duplicidad se evalúa más abajo, una
        // sola vez, junto con el resto de los checks.
        if (_validationPolicy.DuplicateActiveProcedure == TramiteValidationMode.Block)
        {
            var duplicateBeforeQuery = await FindDuplicateActiveProcedureAsync(
                modalidad.Value, request.TenantId, vin, plate, ct);
            if (duplicateBeforeQuery is not null)
                return (null, InitialProcedureValidationGate.DuplicateActiveProcedure, duplicateBeforeQuery, null);
        }

        var tenantOverride = await overrideProvider.GetAsync(request.TenantId, ct);
        // HU #11199 — en matrícula la secretaría YA se conoce en el paso 1, así que las políticas por OT
        // se resuelven contra el organismo real y el semáforo del paso 1 deja de divergir del que se
        // persiste al crear el trámite. En traspaso el OT aún no se conoce (lo trae el RUNT en la
        // consulta que viene justo después) y ambas políticas siguen cayendo a su default —permisivo en
        // restricciones, defaults por criterio en bloqueo—, igual que antes.
        var restrictions = await restrictionPolicy.GetAsync(request.TenantId, secretaria?.Id, ct);
        var blockingRules = await _blockingPolicy.GetAsync(request.TenantId, secretaria?.Id, ct);

        var checks = new List<PreflightCheckDto>();
        var providersUsed = new SortedSet<string>(StringComparer.Ordinal);

        // Tramo del vehículo aislado: es el ÚNICO que se guarda para reusar al crear el trámite.
        var vehicleChecks = new List<PreflightCheckDto>();
        var vehicleProviders = new SortedSet<string>(StringComparer.Ordinal);
        var vehicleFields = await RunPreflightHandler.RunVehiculoAsync(
            chainResolver,
            vehicleChecks,
            vehicleProviders,
            esMatricula ? ConsultationKind.VehicleVin : ConsultationKind.VehiclePlate,
            instanceId: Guid.Empty,
            request.TenantId,
            vin,
            plate,
            fieldValues,
            tenantOverride,
            precomputed: null,
            ct);

        checks.AddRange(vehicleChecks);
        foreach (var p in vehicleProviders)
            providersUsed.Add(p);

        // HU #11200 — TRASPASO: el organismo no se elige, lo impone el RUNT (donde está matriculado el
        // vehículo). Lo que se adelanta al paso 1 es la comprobación de que ahí se puede radicar. Va
        // aquí, justo después de la consulta del vehículo y antes de los comparendos, para no gastar
        // consultas de un trámite que no va a poder seguir.
        if (!esMatricula)
        {
            var bloqueoOt = await ValidarOrganismoDelRuntAsync(request.TenantId, vehicleFields, ct)
                .ConfigureAwait(false);
            if (bloqueoOt is not null)
                return (null, bloqueoOt, null, null);
        }

        VehicleStateBlock? vehicleStateBlock = null;

        if (esMatricula)
        {
            vehicleStateBlock = await DetectVinYaMatriculadoAsync(checks, request.TenantId, vin, ct);
        }
        else if (restrictions.IsDisabled(ConsultationRestrictionKinds.Fines))
        {
            RunPreflightHandler.AddOmittedCheck(
                checks, "simit_omitida", "Consulta de comparendos omitida", "No se consultaron los comparendos");
        }
        else
        {
            // Todavía no hay actores capturados (llegan en pasos posteriores): los checks quedan en
            // `unknown`, exactamente como hoy en la primera corrida del preflight del paso 1.
            await RunPreflightHandler.RunSimitAsync(
                registry, checks, providersUsed, "simit_comprador", "SIMIT comprador", null, tenantOverride, ct);
            await RunPreflightHandler.RunSimitAsync(
                registry, checks, providersUsed, "simit_vendedor", "SIMIT vendedor", null, tenantOverride, ct);
        }

        RunPreflightHandler.AplicarPoliticaDeBloqueo(checks, blockingRules);

        vehicleStateBlock ??= RunPreflightHandler.EndurecerEstadoVehiculoMatricula(
            checks, modalidad, _validationPolicy.VehicleRegistrationState);
        if (vehicleStateBlock is not null)
            return (null, VehicleStatePolicy.ErrorCode, null, vehicleStateBlock);

        // Cambio de carrocería sobre un vehículo que el RUNT no reporta con ninguna. Se comprueba AQUÍ
        // y no solo al crear el trámite porque este es el momento en que el gestor todavía puede
        // escoger otro tipo: enterarse después es enterarse con el expediente ya abierto. Sin respuesta
        // del proveedor no se bloquea (ver VehicleBodyTypePolicy).
        if (_validationPolicy.VehicleBodyTypeRequired != TramiteValidationMode.Off)
        {
            var bodyTypeBlock = VehicleBodyTypePolicy.Evaluar(
                procedureType?.Code,
                consultaRespondio: vehicleFields.Count > 0,
                carroceriaReportada: vehicleFields
                    .FirstOrDefault(f => string.Equals(
                        f.FieldKey, VehicleBodyTypePolicy.BodyTypeFieldKey, StringComparison.OrdinalIgnoreCase))
                    ?.ValueText);

            if (bodyTypeBlock is not null)
            {
                if (_validationPolicy.VehicleBodyTypeRequired == TramiteValidationMode.Block)
                    return (null, VehicleBodyTypePolicy.ErrorCode, null, null);

                checks.Add(RunPreflightHandler.BuildCarroceriaAusenteCheck());
            }
        }

        // Segunda pasada: la llave pudo ocuparse mientras corría la consulta externa (otro operador
        // abriendo el mismo VIN/placa). Barato (solo BD) y cierra la ventana de carrera.
        // HU #10970 — en modo off no se evalúa; en modo warn el hallazgo va como check amarillo.
        if (_validationPolicy.DuplicateActiveProcedure != TramiteValidationMode.Off)
        {
            var duplicateId = await FindDuplicateActiveProcedureAsync(modalidad.Value, request.TenantId, vin, plate, ct);
            if (duplicateId is not null)
            {
                if (_validationPolicy.DuplicateActiveProcedure == TramiteValidationMode.Block)
                    return (null, InitialProcedureValidationGate.DuplicateActiveProcedure, duplicateId, null);

                checks.Add(RunPreflightHandler.BuildDuplicidadCheck(duplicateId.Value));
            }
        }

        // El endurecimiento de `estado_vehiculo` puede haber reescrito checks del tramo del vehículo:
        // se guardan los ya reescritos para que el snapshot persistido al crear sea idéntico al visto.
        var vehicleKeys = vehicleChecks.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var snapshot = new PreflightVehicleSnapshot(
            checks.Where(c => vehicleKeys.Contains(c.Key)).ToList(),
            vehicleFields,
            vehicleProviders.ToList());

        var token = previewStore.Save(request.TenantId, snapshot);
        var overall = RunPreflightHandler.ComposeOverall(checks);
        var provider = providersUsed.Count == 0 ? null : string.Join(",", providersUsed);

        return (
            new PreflightPreviewDto(
                token,
                overall,
                checks,
                provider,
                DateTimeOffset.UtcNow,
                vehicleFields.Select(f => new PreflightPreviewFieldDto(f.FieldKey, f.ValueText, f.ValueJson)).ToList()),
            null,
            null,
            null);
    }

    /// <summary>
    /// HU #11200 (AC1/AC2/AC3) — comprueba que el organismo donde el RUNT dice que está matriculado el
    /// vehículo sirve para radicar: activo en el catálogo y con grant vigente (resolver), y operativo en
    /// la plataforma (compuerta). Devuelve el código de bloqueo, o <c>null</c> si se puede continuar.
    ///
    /// <para>Si el RUNT no devolvió el nombre del organismo NO se bloquea: no hay nada que validar y
    /// negarle el trámite al gestor por un dato que no vino sería castigarlo por un fallo ajeno. Ese caso
    /// lo sigue cubriendo la comprobación de la radicación, que es la que no se puede eludir.</para>
    /// </summary>
    private async Task<string?> ValidarOrganismoDelRuntAsync(
        Guid tenantId,
        IReadOnlyList<HydratedField> vehicleFields,
        CancellationToken ct)
    {
        var runtName = vehicleFields
            .FirstOrDefault(f => string.Equals(f.FieldKey, "transit_office_name", StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        if (string.IsNullOrWhiteSpace(runtName))
            return null;

        var organismo = await transitOfficeResolver
            .ResolveEnabledByNameAsync(tenantId, runtName, ct)
            .ConfigureAwait(false);

        return organismo is null
            || !await _otOperability.IsOperableAsync(organismo.Id, ct).ConfigureAwait(false)
            ? TransitOfficeSelectionPolicy.UnavailableErrorCode
            : null;
    }

    /// <summary>
    /// CF-03 (HU #10877, AC2) sin instancia: VIN con matrícula APROBADA en FLIT → bloqueo duro; con
    /// una previa EN PROCESO → check informativo que ofrece la ruta de traspaso (HU #10538). La señal
    /// <c>vin_conflicto_traspaso</c> no se puede fijar aquí (no hay field_values): la escribe el
    /// preflight al crear el trámite.
    /// </summary>
    private async Task<VehicleStateBlock?> DetectVinYaMatriculadoAsync(
        List<PreflightCheckDto> checks,
        Guid tenantId,
        string? vin,
        CancellationToken ct)
    {
        var vinNorm = VinNormalizer.Normalize(vin);
        if (vinNorm is null)
            return null;

        var conflicto = VinPolicyEvaluator.EvaluarConflicto(
            await repo.FindTramitesByVinAsync(tenantId, vinNorm, Guid.Empty, ct));

        if (conflicto is null)
            return null;

        // HU #10970 — mismo criterio que RunPreflightHandler.DetectVinConflictoTraspasoAsync: fuera de
        // modo block, la matrícula APROBADA degrada al check informativo de la HU #10538 en vez de
        // bloquear.
        if (conflicto.Code == VinConflictCode.TramiteMatriculaCompletada
            && _validationPolicy.VehicleRegistrationState == TramiteValidationMode.Block)
        {
            return new VehicleStateBlock(
                VehicleStatePolicy.VehicleStatusAprobadoFlit,
                VehicleStatePolicy.ProcedureTypeMatriculaInicial,
                VehicleStateSource.Flit);
        }

        checks.Add(RunPreflightHandler.BuildVinMatriculaCheck(conflicto.ExistingTramite));
        return null;
    }

    /// <summary>
    /// CF-01 (HU #10876) sin instancia — misma política y misma activación por familia que el preflight
    /// de la instancia, con <c>Guid.Empty</c> como exclusión (no hay trámite propio que excluir).
    /// </summary>
    private async Task<Guid?> FindDuplicateActiveProcedureAsync(
        ProcedureFamily modalidad,
        Guid tenantId,
        string? vin,
        string? plate,
        CancellationToken ct)
    {
        if (modalidad == ProcedureFamily.Matriculas)
        {
            var vinNorm = VinNormalizer.Normalize(vin);
            if (vinNorm is null)
                return null;

            var existentes = await repo.FindTramitesByVinAsync(tenantId, vinNorm, Guid.Empty, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        if (modalidad == ProcedureFamily.Traspaso)
        {
            var placaNorm = plate?.ToUpperInvariant();
            if (string.IsNullOrEmpty(placaNorm))
                return null;

            var existentes = await repo.FindTramitesByPlacaAsync(tenantId, placaNorm, Guid.Empty, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        return null;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
