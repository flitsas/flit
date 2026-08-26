using System.Globalization;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Un check del semáforo preflight, en la forma congelada del contrato. <see cref="Details"/> es
/// aditivo (opcional): hoy lleva el detalle de comparendos de un check de multas para listarlo bajo
/// la advertencia; el resto de checks lo deja en <c>null</c>. Se serializa en el snapshot y viaja al
/// frontend; los snapshots viejos (sin el campo) deserializan a <c>null</c> sin romper.
/// </summary>
public sealed record PreflightCheckDto(
    string Key,
    string Label,
    string Status,
    string Source,
    string? Message,
    IReadOnlyList<FineDetail>? Details = null);

/// <summary>
/// Snapshot preflight server-driven. <c>Overall</c> ∈ {green|yellow|red} (DI-1: 'yellow', no 'amber').
/// </summary>
public sealed record PreflightSnapshotDto(
    string Overall,
    IReadOnlyList<PreflightCheckDto> Checks,
    string? Provider,
    DateTimeOffset CreatedAt);

/// <summary>
/// Orquesta el preflight: corre los providers RELEVANTES por modalidad (reusa el registry
/// de Slice 5), compone un único snapshot con la regla del dominio (cualquier fail→red,
/// algún warn→yellow, resto→green; unknown no impide green) y lo persiste en
/// <c>procedure_instance_preflight_snapshots</c>. Degrada (no 500) si un provider falla:
/// los providers ya devuelven checks unknown/yellow en error de transporte.
///
/// <para><b>Providers-por-modalidad (hardcode documentado):</b> los <c>consultation_templates</c>
/// resuelven UN provider por template (1:1), no el fan-out por modalidad. La relación
/// "qué providers corren en el preflight de cada modalidad" es lógica de negocio del wizard,
/// no metadata de template; por eso se decide aquí:
/// <list type="bullet">
/// <item>matrícula → vehículo por VIN (verifik): SOAT/RTM/gravámenes.</item>
/// <item>traspaso → vehículo por placa (verifik) + SIMIT comprador + SIMIT vendedor + RNMC comprador.</item>
/// </list>
/// Cada provider recibe un <see cref="ConsultationContext"/> a medida (vin/placa de field_values,
/// documento de comprador/vendedor de los actores), sin tocar los providers de Slice 5.</para>
/// </summary>
public sealed class RunPreflightHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry,
    IConsultationProviderChainResolver chainResolver,
    IConsultationTenantOverrideProvider overrideProvider,
    IConsultationRestrictionPolicy restrictionPolicy,
    ITransitOfficeResolver transitOfficeResolver,
    IConsultationBlockingPolicy? blockingPolicy = null,
    TramiteValidationPolicy? validationPolicy = null)
{
    // FEATURE 05 — política de bloqueo por criterio y OT (default permisivo con los defaults por
    // criterio cuando no se inyecta, p. ej. tests que no ejercitan la configurabilidad).
    private readonly IConsultationBlockingPolicy _blockingPolicy =
        blockingPolicy ?? NullConsultationBlockingPolicy.Instance;

    // HU #10970 — modo por ambiente de CF-01/CF-03. Sin inyectar ⇒ ambas en bloqueo duro, el
    // comportamiento previo a esta historia (mismo criterio que _blockingPolicy con su null-object).
    private readonly TramiteValidationPolicy _validationPolicy =
        validationPolicy ?? TramiteValidationPolicy.BlockAll;

    // FEATURE 05 — el proveedor de comparendos ya no es una constante: lo resuelve
    // FinesProviderResolver por (fuente del tenant, tipo de persona del actor).
    private const string ConsultationSource = "consultation";
    private const string SystemSource = "system";
    private const string FieldVinConflictoTraspaso = "vin_conflicto_traspaso";
    private const string CheckVinMatricula = "vin_matricula";
    private const string CheckSimitOmitida = "simit_omitida";
    // HU #10970 — check de duplicidad en modo advertencia. NO está en CriterioDeCheck: no es un criterio
    // configurable de FEATURE 05, así que AplicarPoliticaDeBloqueo nunca reescribe su severidad.
    private const string CheckDuplicidadTramite = "duplicidad_tramite";
    // Tampoco es un CriterioDeCheck: la ausencia de carrocería no es un hallazgo de proveedor que la
    // compañía pueda relajar por OT, es una precondición del tipo de trámite.
    private const string CheckCarroceriaAusente = "carroceria_ausente";
    // Tampoco es un CriterioDeCheck: no es un hallazgo del proveedor que la compañía pueda relajar
    // por OT, es una precondición del tipo de trámite.
    private const string CheckPrendaAusente = "prenda_ausente";

    // A4/B4 (HU #10673, ADR-0029) — atributos del vehículo que el operador puede TRANSFORMAR durante el
    // trámite (color/combustible). Cada valor efectivo (el que va al FUR) mapea con su flag de cambio
    // declarado; el snapshot RUNT vive en "{key}_runt". Ver UpsertTransformationAwareField.
    private const string RuntSnapshotSuffix = "_runt";
    private static readonly Dictionary<string, string> TransformationFlagByEffectiveKey =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vehicle_color"] = "cambio_color",
            ["vehicle_fuel"] = "cambio_combustible",
            ["vehicle_body_type"] = "cambio_carroceria",
        };

    public Task<(PreflightSnapshotDto? Result, string? Error, Guid? ExistingProcedureInstanceId, VehicleStateBlock? VehicleState)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
        => HandleAsync(id, tenantId, precomputedVehicle: null, ct);

    /// <summary>
    /// CF-02 (HU #10879/#10883) — variante que REUSA una consulta de vehículo ya resuelta en el paso 1
    /// (<see cref="RunPreflightPreviewHandler"/>, cuando el trámite todavía no existía). Todo lo demás
    /// —duplicidad, precondición registral, política de bloqueo, hidratación y persistencia del
    /// snapshot— corre EXACTAMENTE igual sobre la instancia recién creada; lo único que se salta es la
    /// segunda llamada al proveedor externo. Con <paramref name="precomputedVehicle"/> en <c>null</c>
    /// el comportamiento es el histórico (consulta fresca).
    /// </summary>
    public async Task<(PreflightSnapshotDto? Result, string? Error, Guid? ExistingProcedureInstanceId, VehicleStateBlock? VehicleState)> HandleAsync(
        Guid id,
        Guid tenantId,
        PreflightVehicleSnapshot? precomputedVehicle,
        CancellationToken ct)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found", null, null);

        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft", null, null);

        var modalidad = instance.Family;

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var vin = Get(fieldValues, "vin");
        var plate = Get(fieldValues, "plate");
        var comprador = ActorOf(instance, "comprador");
        var vendedor = ActorOf(instance, "vendedor");

        var checks = new List<PreflightCheckDto>();
        var providersUsed = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlyList<HydratedField> vehicleFields;
        // CF-03 (HU #10877) — bloqueo DURO "vehículo ya matriculado" (doble fuente RUNT/FLIT). Se
        // resuelve durante la rama de matrícula (ver DetectVinConflictoTraspasoAsync más abajo) y/o al
        // endurecer el check estado_vehiculo (ver EndurecerEstadoVehiculoMatricula). Solo aplica a
        // Matrícula Inicial: en traspaso queda siempre null.
        VehicleStateBlock? vehicleStateBlock = null;

        // HU #10478: la cadena de proveedores (Kyverum-first → Verifik) y el presupuesto de failover
        // salen de la config del tenant; null ⇒ defaults globales.
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);

        var otId = TransitOfficeIdFromFieldValues(instance);

        // HU #10760 — comparendos que la compañía inhabilitó para ese OT (opt-out: default se consulta;
        // enabled=false lo inhabilita). El RNMC ya NO corre aquí: se desacopló a una consulta dedicada
        // del paso final (ver RunRnmcConsultHandler), porque la fecha de expedición se captura después.
        var restrictions = await restrictionPolicy.GetAsync(tenantId, otId, ct);
        var finesDisabled = restrictions.IsDisabled(ConsultationRestrictionKinds.Fines);

        // FEATURE 05 — reglas de bloqueo por criterio para el mismo par (tenant, OT): deciden si un
        // hallazgo negativo bloquea (fail→rojo) o solo advierte (warn→amarillo). Se aplican sobre los
        // checks antes de componer el overall (ver AplicarPoliticaDeBloqueo).
        var blockingRules = await _blockingPolicy.GetAsync(tenantId, otId, ct);

        if (modalidad == ProcedureFamily.Traspaso)
        {
            // Vehículo por placa (requiere documento del propietario actual). El doc del
            // propietario se persiste en field_values en el paso "consulta" (puede llegar
            // antes de que exista el actor vendedor); de ahí lo toma el provider.
            vehicleFields = await RunVehiculoAsync(chainResolver, checks, providersUsed, ConsultationKind.VehiclePlate, instance.Id, tenantId, vin, plate, fieldValues, tenantOverride, precomputedVehicle, ct);
            // Comparendos del comprador y del vendedor. El proveedor se resuelve por actor según la
            // fuente configurada por la compañía (FEATURE 05).
            if (finesDisabled)
            {
                AddOmittedCheck(checks, CheckSimitOmitida, "Consulta de comparendos omitida", "No se consultaron los comparendos");
            }
            else
            {
                await RunSimitAsync(registry, checks, providersUsed, "simit_comprador", "SIMIT comprador", comprador, tenantOverride, ct);
                await RunSimitAsync(registry, checks, providersUsed, "simit_vendedor", "SIMIT vendedor", vendedor, tenantOverride, ct);
            }
        }
        else if (modalidad == ProcedureFamily.Otros)
        {
            // ADR-0050 — la familia OTROS opera sobre un vehículo YA matriculado: entra por placa
            // (gate_profile.entryMode = PLATE) igual que el traspaso. Antes caía en el `else` de
            // abajo y se consultaba por VIN, de modo que un blindaje o un duplicado de tarjeta —que
            // nunca traen VIN, porque el paso 1 pide placa— corrían el pre-vuelo contra un
            // identificador vacío y el semáforo salía en gris sin que nada fallara.
            //
            // Interviene UN SOLO actor, el propietario inscrito, persistido como `comprador` (el
            // modelo no tiene rol 'propietario'). No hay parte saliente que consultar: pedir un
            // SIMIT de vendedor aquí devolvería siempre vacío y además insinuaría una compraventa.
            vehicleFields = await RunVehiculoAsync(chainResolver, checks, providersUsed, ConsultationKind.VehiclePlate, instance.Id, tenantId, vin, plate, fieldValues, tenantOverride, precomputedVehicle, ct);
            if (finesDisabled)
            {
                AddOmittedCheck(checks, CheckSimitOmitida, "Consulta de comparendos omitida", "No se consultaron los comparendos");
            }
            else
            {
                await RunSimitAsync(registry, checks, providersUsed, "simit_comprador", "SIMIT propietario", comprador, tenantOverride, ct);
            }
        }
        else
        {
            // Matrícula inicial: vehículo por VIN (primera matrícula, sin propietario previo).
            vehicleFields = await RunVehiculoAsync(chainResolver, checks, providersUsed, ConsultationKind.VehicleVin, instance.Id, tenantId, vin, plate, fieldValues, tenantOverride, precomputedVehicle, ct);
            // R3 (HU #10538) / CF-03 (HU #10877, AC2): si el VIN ya tiene una matrícula previa en el
            // tenant. Con la única previa APROBADA (fuente FLIT) es bloqueo DURO CF-03 (ver
            // VehicleStateBlock); con una previa EN PROCESO se conserva el check informativo que
            // ofrece la ruta de traspaso (HU #10538).
            vehicleStateBlock = await DetectVinConflictoTraspasoAsync(checks, instance, tenantId, vin, ct);
        }

        // El RNMC (medidas correctivas) ya NO se consulta aquí ni deja señal: corre en su consulta
        // dedicada del paso final (RunRnmcConsultHandler), cuando la fecha de expedición ya se capturó.

        // Una sola consulta a Verifik alimenta AMBAS secciones: el proveedor del vehículo ya
        // devolvió los atributos del RUNT (marca/línea/color/…). Los persistimos en field_values
        // (source="consultation") para la tarjeta "Datos del vehículo", evitando una segunda
        // consulta dedicada. Idempotente: upsert por field_key.
        UpsertHydratedFields(instance, tenantId, RedirigirOrganismoDelRunt(instance, vehicleFields));

        // B11 (HU #10659) — en TRASPASO y en la familia OTROS el vehículo ya tiene OT en RUNT: tras
        // hidratar transit_office_name, resolver el OT habilitado de la empresa y fijar también
        // id/code/city. El OT queda fijado desde el RUNT.
        await AutoBindTransitOfficeFromRuntAsync(instance, tenantId, ct);

        // FEATURE 05 — ajusta la severidad de cada criterio configurable (soat/rtm/estado/fines/rnmc)
        // según la política de la compañía para el OT destino. Va ANTES de la relajación de matrícula
        // (piso: el estado del 0km nunca bloquea) y de componer el overall.
        AplicarPoliticaDeBloqueo(checks, blockingRules);

        // CF-03 (HU #10877, AC1/AC3) — endurece estado_vehiculo para Matrícula Inicial: ACTIVO (RUNT
        // reporta el vehículo ya matriculado) y unknown (RUNT sin dato) pasan a bloqueo DURO; el fail
        // "clásico" (p. ej. REGISTRADO, esperado en un 0 km) conserva la degradación histórica a warn
        // (HU #10538). Si AC2 (fuente FLIT) ya bloqueó arriba, no hace falta re-endurecer.
        // HU #10970 — en modo warn/off el endurecimiento no bloquea (ver EndurecerEstadoVehiculoMatricula).
        vehicleStateBlock ??= EndurecerEstadoVehiculoMatricula(
            checks, modalidad, _validationPolicy.VehicleRegistrationState);
        if (vehicleStateBlock is not null)
            return (null, VehicleStatePolicy.ErrorCode, null, vehicleStateBlock);

        // Cambio de carrocería sobre un vehículo que el RUNT no reporta con ninguna: no hay atributo
        // que sustituir, así que el trámite no puede radicarse. Se evalúa sobre lo que devolvió ESTA
        // consulta (no sobre lo persistido) para no confundir «el RUNT dice que no tiene» con «el RUNT
        // no contestó»: sin respuesta no se bloquea nunca. En modo warn el hallazgo viaja como check
        // amarillo dentro del snapshot; en modo off ni se evalúa.
        if (_validationPolicy.VehicleBodyTypeRequired != TramiteValidationMode.Off)
        {
            var bodyTypeBlock = VehicleBodyTypePolicy.Evaluar(
                instance.ProcedureType?.Code,
                consultaRespondio: vehicleFields.Count > 0,
                carroceriaReportada: vehicleFields
                    .FirstOrDefault(f => string.Equals(
                        f.FieldKey, VehicleBodyTypePolicy.BodyTypeFieldKey, StringComparison.OrdinalIgnoreCase))
                    ?.ValueText);

            if (bodyTypeBlock is not null)
            {
                if (_validationPolicy.VehicleBodyTypeRequired == TramiteValidationMode.Block)
                    return (null, VehicleBodyTypePolicy.ErrorCode, null, null);

                checks.Add(BuildCarroceriaAusenteCheck());
            }
        }

        // Levantamiento de prenda sobre un vehículo que el RUNT no reporta con gravamen: no hay nada
        // que levantar, y el acreedor del numeral 20 lo precarga justamente ese gravamen. Se lee del
        // check ya compuesto, no de los field_values: el semáforo es quien sabe si el proveedor
        // afirmó la ausencia o simplemente no trajo el dato.
        if (_validationPolicy.VehiclePrendaRequired != TramiteValidationMode.Off)
        {
            var prendaBlock = VehiclePrendaPolicy.Evaluar(
                instance.ProcedureType?.Code,
                EstadoDelCheck(checks, VehiclePrendaPolicy.GravamenCheckKey));

            if (prendaBlock is not null)
            {
                if (_validationPolicy.VehiclePrendaRequired == TramiteValidationMode.Block)
                    return (null, VehiclePrendaPolicy.ErrorCode, null, null);

                checks.Add(BuildPrendaAusenteCheck());
            }
        }

        // CF-01 (HU #10876) — bloqueo DURO de duplicidad EN PROCESO por familia (VIN en Matrícula
        // Inicial, placa en Traspaso), evaluado ANTES de componer el overall y persistir el snapshot.
        // Si hay coincidencia, el preflight NO se persiste: se devuelve el bloqueo 409 con el id del
        // trámite existente. Independiente de CF-03 (registral) — ambos deben dar luz verde.
        // HU #10970 — en modo off ni se consulta el repositorio; en modo warn no se corta el flujo y el
        // hallazgo viaja como check amarillo dentro del snapshot que sí se persiste.
        if (_validationPolicy.DuplicateActiveProcedure != TramiteValidationMode.Off)
        {
            var duplicateId = await FindDuplicateActiveProcedureAsync(instance, modalidad, tenantId, vin, plate, ct);
            if (duplicateId is not null)
            {
                if (_validationPolicy.DuplicateActiveProcedure == TramiteValidationMode.Block)
                    return (null, InitialProcedureValidationGate.DuplicateActiveProcedure, duplicateId, null);

                checks.Add(BuildDuplicidadCheck(duplicateId.Value));
            }
        }

        // Composición del overall con la regla del dominio.
        var overall = ComposeOverall(checks);
        var provider = providersUsed.Count == 0 ? null : string.Join(",", providersUsed);
        var now = DateTimeOffset.UtcNow;

        var snapshot = new ProcedureInstancePreflightSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            Overall = overall,
            Checks = JsonSerializer.Serialize(checks),
            Provider = provider,
            CreatedAt = now,
        };

        await repo.AddPreflightSnapshotAsync(snapshot, ct);
        await repo.SaveChangesAsync(ct);

        return (new PreflightSnapshotDto(overall, checks, provider, now), null, null, null);
    }

    /// <summary>
    /// CF-01 (HU #10876) — resuelve el bloqueo DURO de duplicidad EN PROCESO por familia. ACTIVACIÓN
    /// HARDCODED por familia (no depende de <c>gate_profile.validateDuplicateProcedure</c>, honrando
    /// CF-01 al pie de la letra): SOLO Matrícula Inicial (llave VIN) y Traspaso (llave placa) activan
    /// el bloqueo; el resto de familias permite múltiples instancias en proceso. Devuelve el
    /// <c>Id</c> del primer trámite EN PROCESO encontrado, o <c>null</c> si la llave está libre.
    /// </summary>
    private async Task<Guid?> FindDuplicateActiveProcedureAsync(
        ProcedureInstance instance,
        ProcedureFamily? modalidad,
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

            var existentes = await repo.FindTramitesByVinAsync(tenantId, vinNorm, instance.Id, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        if (modalidad == ProcedureFamily.Traspaso)
        {
            var placaNorm = plate?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(placaNorm))
                return null;

            var existentes = await repo.FindTramitesByPlacaAsync(tenantId, placaNorm, instance.Id, ct);
            return DuplicateActiveProcedurePolicy.FindActiveDuplicate(
                existentes.Select(e => (e.Id, e.Estado, e.SubsanacionActiva)).ToList());
        }

        return null;
    }

    /// <summary>
    /// Regla del dominio: cualquier <c>fail</c> o <c>error</c> → red; algún <c>warn</c> → yellow;
    /// resto → green. <c>unknown</c> NO impide green (dato ausente no crítico no bloquea).
    /// <c>error</c> = proveedor no verificable: pinta red igual que <c>fail</c>, pero además el
    /// wizard lo trata como bloqueo DURO (no subsanable) vía blocker propio. Sin checks → green.
    /// </summary>
    public static string ComposeOverall(IReadOnlyList<PreflightCheckDto> checks)
    {
        var hasFail = false;
        var hasWarn = false;
        foreach (var c in checks)
        {
            switch (c.Status)
            {
                case "fail":
                case "error":
                    hasFail = true;
                    break;
                case "warn":
                    hasWarn = true;
                    break;
            }
        }

        if (hasFail)
            return "red";
        if (hasWarn)
            return "yellow";
        return "green";
    }

    /// <summary>
    /// HU #10760 — deja constancia de una consulta que NO se corrió porque la compañía la inhabilitó
    /// para el OT destino. UN solo check por tipo de consulta (la restricción es del par tenant+OT,
    /// no del rol), con Source="system": no hubo proveedor que la atendiera.
    ///
    /// Status <c>unknown</c> y NO <c>warn</c>: <see cref="ComposeOverall"/> solo pinta amarillo con
    /// warn, así que unknown PRESERVA el verde. Con warn, toda compañía con una restricción activa
    /// viviría en amarillo permanente y el amarillo dejaría de significar "hallazgo".
    /// </summary>
    internal static void AddOmittedCheck(
        List<PreflightCheckDto> checks,
        string key,
        string label,
        string noSeConsulto) =>
        checks.Add(new PreflightCheckDto(key, label, "unknown", SystemSource,
            $"{noSeConsulto}: la compañía tiene esta consulta inhabilitada para el organismo de tránsito de destino."));

    /// <summary>
    /// FEATURE 05 — reescribe la severidad de los checks de los criterios configurables según la
    /// política de la compañía para el OT destino. Un hallazgo NEGATIVO (status <c>fail</c> o
    /// <c>warn</c>) se coacciona a <c>fail</c> si el criterio bloquea, o a <c>warn</c> si solo
    /// advierte. Nunca toca <c>ok</c>/<c>unknown</c>/<c>error</c>: un dato correcto no se inventa como
    /// hallazgo y un proveedor caído (<c>error</c>) sigue siendo bloqueo DURO no configurable.
    ///
    /// Con los defaults (soat/rtm/estado bloquean; fines/rnmc advierten) y sin filas configuradas es
    /// un no-op exacto → preserva el comportamiento previo. Reusa el patrón de reescritura
    /// <c>checks[i] = c with { Status = ... }</c> de <see cref="RelajarEstadoVehiculoMatricula"/>.
    /// </summary>
    internal static void AplicarPoliticaDeBloqueo(
        List<PreflightCheckDto> checks,
        ConsultationBlockingRules rules)
    {
        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            if (c.Status != "fail" && c.Status != "warn")
                continue;

            var criterion = CriterioDeCheck(c.Key);
            if (criterion is null)
                continue;

            var target = rules.Blocks(criterion) ? "fail" : "warn";
            if (c.Status != target)
                checks[i] = c with { Status = target };
        }
    }

    /// <summary>
    /// Mapea la clave de un check del preflight a su criterio configurable (FEATURE 05), o <c>null</c>
    /// si el check no es configurable. Las claves de comparendos y RNMC llevan prefijo de actor/rol
    /// (p. ej. <c>simit_comprador_multas</c>, <c>rnmc_comprador_medidas_correctivas</c>), por eso se
    /// reconocen por sufijo — mismo criterio que <c>WizardStateQuery</c> deriva <c>simit_multas</c>.
    /// </summary>
    private static string? CriterioDeCheck(string key) => key switch
    {
        "soat" => ConsultationBlockingCriteria.Soat,
        "tecnomecanica" => ConsultationBlockingCriteria.Rtm,
        "estado_vehiculo" => ConsultationBlockingCriteria.EstadoVehiculo,
        _ when key.EndsWith("_multas", StringComparison.Ordinal)
            || key.EndsWith("_acuerdos_pago", StringComparison.Ordinal) => ConsultationBlockingCriteria.Fines,
        _ when key.EndsWith("_medidas_correctivas", StringComparison.Ordinal) => ConsultationBlockingCriteria.Rnmc,
        _ => null,
    };

    /// <summary>
    /// CF-03 (HU #10877, AC1/AC3) — en MATRÍCULA INICIAL endurece el check <c>estado_vehiculo</c> según
    /// lo que reporte el RUNT (sustituye la antigua <c>RelajarEstadoVehiculoMatricula</c>, que degradaba
    /// TODO <c>fail</c> a <c>warn</c> sin distinguir el motivo):
    /// <list type="bullet">
    /// <item><c>ok</c> (RUNT reporta <c>"ACTIVO"</c>): el vehículo YA está matriculado/circulando →
    /// AC1, bloqueo DURO (<see cref="VehicleStatePolicy.VehicleStatusActivoRunt"/>).</item>
    /// <item><c>unknown</c> (RUNT no respondió o no trajo el estado): AC3, bloqueo DURO hasta poder
    /// confirmar el estado (<see cref="VehicleStatePolicy.VehicleStatusDesconocido"/>) — un dato no
    /// verificable NO debe preservar el semáforo en verde para esta familia.</item>
    /// <item><c>fail</c> con un estado explícito distinto de ACTIVO (p. ej. <c>"REGISTRADO"</c>, el
    /// esperado para un 0 km sin matricular aún): se conserva la degradación histórica a <c>warn</c>
    /// (informativo, HU #10538) — NO es "ya matriculado".</item>
    /// </list>
    /// En TRASPASO no se toca: se exige <c>"ACTIVO"</c> (vehículo en circulación) y el <c>fail</c> se
    /// mantiene intacto (comportamiento previo).
    ///
    /// <para>HU #10970 — <paramref name="mode"/> decide qué pasa con los dos casos de bloqueo:
    /// <c>block</c> (default) los deja como arriba; <c>warn</c> los reescribe a <c>warn</c> con el
    /// mismo mensaje y NO devuelve bloqueo (el operador ve el hallazgo en amarillo y sigue);
    /// <c>off</c> no toca esos dos casos (ni señal). La degradación <c>fail</c>→<c>warn</c> del último
    /// caso se conserva en los tres modos: es de la HU #10538, NO de CF-03.</para>
    /// </summary>
    internal static VehicleStateBlock? EndurecerEstadoVehiculoMatricula(
        List<PreflightCheckDto> checks,
        ProcedureFamily? modalidad,
        TramiteValidationMode mode = TramiteValidationMode.Block)
    {
        if (modalidad != ProcedureFamily.Matriculas)
            return null;

        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            if (!string.Equals(c.Key, "estado_vehiculo", StringComparison.Ordinal))
                continue;

            switch (c.Status)
            {
                case "ok":
                    if (mode == TramiteValidationMode.Off)
                        break;

                    checks[i] = c with
                    {
                        Status = mode == TramiteValidationMode.Block ? "fail" : "warn",
                        Message = "El vehículo ya se encuentra matriculado según el RUNT.",
                    };
                    if (mode == TramiteValidationMode.Warn)
                        break;

                    return new VehicleStateBlock(
                        VehicleStatePolicy.VehicleStatusActivoRunt,
                        VehicleStatePolicy.ProcedureTypeMatriculaInicial,
                        VehicleStateSource.Runt);

                case "unknown":
                    if (mode == TramiteValidationMode.Off)
                        break;

                    checks[i] = c with
                    {
                        Status = mode == TramiteValidationMode.Block ? "fail" : "warn",
                        Message = "No fue posible confirmar el estado del vehículo en el RUNT. Vuelve a intentarlo.",
                    };
                    if (mode == TramiteValidationMode.Warn)
                        break;

                    return new VehicleStateBlock(
                        VehicleStatePolicy.VehicleStatusDesconocido,
                        VehicleStatePolicy.ProcedureTypeMatriculaInicial,
                        VehicleStateSource.RuntDesconocido);

                case "fail":
                    checks[i] = c with { Status = "warn" };
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// CF-01 (HU #10876) en modo <c>warn</c> (HU #10970) — hallazgo de duplicidad que NO corta el flujo.
    /// Status <c>warn</c> para que <see cref="ComposeOverall"/> pinte amarillo y el operador vea que ya
    /// hay un trámite en curso con la misma llave, con su id para poder retomarlo manualmente. En modo
    /// <c>block</c> este check no existe: ahí la duplicidad viaja como 409 y el snapshot ni se persiste.
    /// </summary>
    internal static PreflightCheckDto BuildDuplicidadCheck(Guid existingProcedureInstanceId) =>
        new(CheckDuplicidadTramite,
            "Trámite duplicado en curso",
            "warn",
            SystemSource,
            "Ya existe un trámite en curso para la misma placa/VIN (id " +
            $"{existingProcedureInstanceId}). El bloqueo por duplicidad está en modo advertencia en este ambiente.");

    /// <summary>
    /// Bloqueo «sin carrocería que cambiar» en modo <c>warn</c>: el hallazgo no corta el flujo pero sí
    /// pinta el semáforo en amarillo, para que el gestor sepa por qué el organismo puede devolverle el
    /// expediente. En modo <c>block</c> este check no existe: allí viaja como 422 y el snapshot ni se
    /// persiste.
    /// </summary>
    /// <summary>Estado del check indicado, o <c>null</c> si el semáforo no llegó a emitirlo.</summary>
    internal static string? EstadoDelCheck(IReadOnlyList<PreflightCheckDto> checks, string key) =>
        checks.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Status;

    /// <summary>
    /// Bloqueo «sin prenda que levantar» en modo <c>warn</c>: no corta el flujo pero pinta el
    /// semáforo en amarillo. En modo <c>block</c> este check no existe: allí viaja como 422.
    /// </summary>
    internal static PreflightCheckDto BuildPrendaAusenteCheck() =>
        new(CheckPrendaAusente,
            "Vehículo sin prenda registrada",
            "warn",
            SystemSource,
            "El RUNT no reporta gravamen sobre este vehículo, así que no hay prenda que levantar. "
            + "El bloqueo está en modo advertencia en este ambiente.");

    internal static PreflightCheckDto BuildCarroceriaAusenteCheck() =>
        new(CheckCarroceriaAusente,
            "Vehículo sin carrocería registrada",
            "warn",
            SystemSource,
            "El RUNT no reporta carrocería para este vehículo, así que no hay carrocería que cambiar. "
            + "El bloqueo está en modo advertencia en este ambiente.");

    /// <summary>
    /// Corre la consulta de vehículo a través de la CADENA de proveedores (HU #10478): Kyverum RUNT
    /// primero, Verifik como fallback, según la config del tenant. El provider concreto decide VIN vs
    /// placa desde los field_values (VIN prioritario); <paramref name="kind"/> selecciona la cadena por
    /// modalidad. Nunca propaga 500: cualquier excepción inesperada se traduce a un check <c>error</c>.
    /// </summary>
    internal static async Task<IReadOnlyList<HydratedField>> RunVehiculoAsync(
        IConsultationProviderChainResolver chainResolver,
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        ConsultationKind kind,
        Guid instanceId,
        Guid tenantId,
        string? vin,
        string? plate,
        Dictionary<string, string?> fieldValues,
        ConsultationTenantOverride? tenantOverride,
        PreflightVehicleSnapshot? precomputed,
        CancellationToken ct)
    {
        // CF-02 — la consulta ya se resolvió en el paso 1 (preview sin instancia): se reusan sus checks
        // y atributos tal cual, sin volver a llamar al proveedor externo.
        if (precomputed is not null)
        {
            checks.AddRange(precomputed.Checks);
            foreach (var p in precomputed.Providers)
                providersUsed.Add(p);
            return precomputed.HydratedFields;
        }

        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(vin)) fv["vin"] = vin;
        if (!string.IsNullOrWhiteSpace(plate)) fv["plate"] = plate;
        // Documento del propietario para la consulta por placa: lo lee el provider
        // vehicle-by-plate de los field_values, donde lo persiste el paso "consulta".
        var ownerDocType = Get(fieldValues, "owner_document_type");
        var ownerDocNumber = Get(fieldValues, "owner_document_number");
        if (!string.IsNullOrWhiteSpace(ownerDocType)) fv["owner_document_type"] = ownerDocType;
        if (!string.IsNullOrWhiteSpace(ownerDocNumber)) fv["owner_document_number"] = ownerDocNumber;

        try
        {
            var ctx = new ConsultationContext(instanceId, tenantId, "vehiculo", fv);
            var result = await chainResolver.ConsultAsync(kind, ctx, tenantOverride, ct);
            providersUsed.Add(result.Provider);
            foreach (var c in result.Checks)
                checks.Add(new PreflightCheckDto(c.Key, c.Label, c.Status, c.Source, c.Message, c.Details));

            return result.HydratedFields;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            checks.Add(new PreflightCheckDto("vehiculo", "Consulta de vehículo", "error", "kyverum_runt",
                "No fue posible verificar la información del vehículo en el RUNT en este momento. Vuelve a intentarlo en unos minutos."));
            return [];
        }
    }

    /// <summary>
    /// FEATURE 05 (HU #10758) — consulta de comparendos del actor. El proveedor ya NO es
    /// <c>verifik_simit</c> fijo: lo elige <see cref="FinesProviderResolver"/> a partir de la fuente
    /// configurada por la compañía (<c>fines_query_source</c>, que viaja en el override del tenant que
    /// el handler ya leyó una vez) y del tipo de persona del actor.
    ///
    /// La resolución es POR ACTOR: en un traspaso con comprador jurídico y vendedor natural, cada uno
    /// va a un proveedor distinto. Por eso las guardas van en este orden — hace falta el actor para
    /// saber a quién preguntarle y con qué proveedor.
    /// </summary>
    internal static async Task RunSimitAsync(
        IConsultationProviderRegistry registry,
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        string fallbackKey,
        string fallbackLabel,
        ActorRef? actor,
        ConsultationTenantOverride? tenantOverride,
        CancellationToken ct)
    {
        // Sin actor no hay a quién consultar NI con qué elegir el externo: se asume natural solo
        // para etiquetar el Source del check con un proveedor coherente.
        var providerKey = FinesProviderResolver.Resolve(
            tenantOverride?.FinesQuerySource,
            actor is null || IsNaturalPerson(actor));

        if (actor is null)
        {
            checks.Add(new PreflightCheckDto(fallbackKey, fallbackLabel, "unknown", providerKey,
                "Actor sin documento para consultar comparendos"));
            return;
        }

        var provider = registry.Resolve(providerKey);
        if (provider is null)
        {
            checks.Add(new PreflightCheckDto(fallbackKey, fallbackLabel, "error", providerKey,
                "No fue posible verificar la información de comparendos en este momento. Vuelve a intentarlo en unos minutos."));
            return;
        }

        providersUsed.Add(providerKey);
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = actor.DocumentType,
            ["owner_document_number"] = actor.DocumentNumber,
        };

        await RunProviderAsync(checks, provider, fv, ct, keyPrefix: fallbackKey);
    }

    // RNMC aplica solo a personas naturales. Usa PersonType (HU #10542) y, si no está seteado,
    // cae al tipo de documento: NIT ⇒ jurídica; cualquier otro ⇒ natural.
    private static bool IsNaturalPerson(ActorRef actor) =>
        ActorPersonTypes.IsNatural(actor.PersonType)
        || (string.IsNullOrWhiteSpace(actor.PersonType)
            && !string.Equals(actor.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ejecuta un provider con un contexto a medida y vuelca sus checks en el snapshot.
    /// Devuelve los <see cref="HydratedField"/> que el provider extrajo de la MISMA respuesta
    /// (p. ej. los atributos del vehículo del RUNT), para persistirlos en field_values sin una
    /// segunda consulta. Cualquier excepción inesperada se traduce a un check <c>error</c> (bloqueo
    /// duro: no se pudo verificar) SIN propagar 500 al caller.
    /// </summary>
    private static async Task<IReadOnlyList<HydratedField>> RunProviderAsync(
        List<PreflightCheckDto> checks,
        IConsultationProvider provider,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken ct,
        string? keyPrefix = null)
    {
        try
        {
            var ctx = new ConsultationContext(Guid.Empty, Guid.Empty, provider.Key, fieldValues);
            var result = await provider.ConsultAsync(ctx, ct);
            foreach (var c in result.Checks)
            {
                var key = keyPrefix is null ? c.Key : $"{keyPrefix}_{c.Key}";
                checks.Add(new PreflightCheckDto(key, c.Label, c.Status, c.Source, c.Message, c.Details));
            }

            return result.HydratedFields;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            var key = keyPrefix ?? "provider";
            checks.Add(new PreflightCheckDto(key, "Consulta no disponible", "error", provider.Key,
                "No fue posible verificar la información en este momento. Vuelve a intentarlo en unos minutos."));
            return [];
        }
    }

    /// <summary>
    /// R3 (HU #10538) / CF-03 (HU #10877, AC2) — en MATRÍCULA INICIAL detecta si el VIN ya tiene una
    /// matrícula previa en el mismo tenant (invariante <see cref="VinPolicyEvaluator"/>):
    /// <list type="bullet">
    /// <item><see cref="VinConflictCode.TramiteMatriculaCompletada"/> (la previa está APROBADA en FLIT):
    /// AC2, bloqueo DURO — un VIN aprobado no puede volver a matricularse. Ya NO es solo informativo:
    /// se devuelve el <see cref="VehicleStateBlock"/> SIN agregar el check informativo ni la señal de
    /// traspaso (el preflight completo se descarta cuando hay bloqueo, ver <c>HandleAsync</c>).</item>
    /// <item><see cref="VinConflictCode.TramiteDuplicado"/> (la previa sigue EN PROCESO): se conserva el
    /// comportamiento HU #10538 — check informativo <see cref="CheckVinMatricula"/> (<c>warn</c>, con
    /// secretaría + fecha) + señal <see cref="FieldVinConflictoTraspaso"/> = <c>true</c> para que el
    /// front ofrezca la ruta de traspaso. Independiente del bloqueo DURO 409 de CF-01
    /// (<see cref="DuplicateActiveProcedurePolicy"/>), que evalúa la misma llave más abajo.</item>
    /// </list>
    /// Sin previas o con la única previa en estado <c>rechazado</c>/<c>anulado</c> no hay conflicto: la
    /// señal previa se limpia (idempotencia entre corridas si el gestor cambió el VIN).
    /// </summary>
    private async Task<VehicleStateBlock?> DetectVinConflictoTraspasoAsync(
        List<PreflightCheckDto> checks,
        ProcedureInstance instance,
        Guid tenantId,
        string? vin,
        CancellationToken ct)
    {
        var vinNorm = VinNormalizer.Normalize(vin);
        var conflicto = vinNorm is null
            ? null
            : VinPolicyEvaluator.EvaluarConflicto(
                await repo.FindTramitesByVinAsync(tenantId, vinNorm, instance.Id, ct));

        if (conflicto is null)
        {
            // Sin conflicto: si una corrida anterior dejó la señal en true (p. ej. el gestor corrigió el
            // VIN), bajarla a false para que el front no siga ofreciendo el traspaso.
            SetSignalIfChanged(instance, tenantId, FieldVinConflictoTraspaso, "false", createIfMissing: false);
            return null;
        }

        // HU #10970 — en modo warn/off el VIN con matrícula APROBADA deja de ser bloqueo duro y vuelve a
        // tratarse como el check informativo de la HU #10538 ("VIN ya matriculado", warn) con su señal de
        // ruta de traspaso. Ese check NO se suprime en modo off porque pertenece a la HU #10538, no a
        // CF-03: apagar CF-03 desactiva su bloqueo, no la información de otra historia.
        if (conflicto.Code == VinConflictCode.TramiteMatriculaCompletada
            && _validationPolicy.VehicleRegistrationState == TramiteValidationMode.Block)
        {
            return new VehicleStateBlock(
                VehicleStatePolicy.VehicleStatusAprobadoFlit,
                VehicleStatePolicy.ProcedureTypeMatriculaInicial,
                VehicleStateSource.Flit);
        }

        checks.Add(BuildVinMatriculaCheck(conflicto.ExistingTramite));

        SetSignalIfChanged(instance, tenantId, FieldVinConflictoTraspaso, "true", createIfMissing: true);
        return null;
    }

    /// <summary>
    /// Check informativo (HU #10538, R3) del VIN con una matrícula previa EN PROCESO: mismo texto tanto
    /// en el preflight de la instancia como en el preview del paso 1 (CF-02), para que el mensaje y la
    /// oferta de "iniciar traspaso" no diverjan entre ambos caminos.
    /// </summary>
    internal static PreflightCheckDto BuildVinMatriculaCheck(VinTramiteExistente previo)
    {
        ArgumentNullException.ThrowIfNull(previo);
        var secretaria = string.IsNullOrWhiteSpace(previo.Secretaria) ? "otra secretaría" : previo.Secretaria!;
        var fecha = previo.FechaRegistro?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cuando = fecha is null ? string.Empty : $" el {fecha}";

        return new PreflightCheckDto(
            CheckVinMatricula,
            "VIN ya matriculado",
            "warn",
            SystemSource,
            $"Este VIN ya tiene una matrícula registrada en {secretaria}{cuando}. " +
            "Puede iniciar un traspaso de este vehículo en lugar de una nueva matrícula.");
    }

    /// <summary>
    /// Upsert idempotente de una señal booleana server-driven en field_values (Source="system"). Mismo
    /// mecanismo de INSERT explícito (PK store-generated) que <see cref="UpsertHydratedFields"/>, pero
    /// para una señal derivada del preflight. Con <paramref name="createIfMissing"/>=false solo actualiza
    /// una señal ya existente (para bajarla a false sin crear filas innecesarias). No-op si el valor no
    /// cambia. Se persiste en el flush de <see cref="HandleAsync"/>.
    /// </summary>
    private void SetSignalIfChanged(
        ProcedureInstance instance,
        Guid tenantId,
        string fieldKey,
        string value,
        bool createIfMissing)
    {
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == fieldKey);
        var now = DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            if (existing.ValueText == value && existing.Source == SystemSource)
                return;
            existing.ValueText = value;
            existing.ValueJson = null;
            existing.Source = SystemSource;
            existing.UpdatedAt = now;
            return;
        }

        if (!createIfMissing)
            return;

        var fieldValue = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            FormFieldId = null,
            FieldKey = fieldKey,
            ValueText = value,
            ValueJson = null,
            Source = SystemSource,
            CreatedAt = now,
        };
        instance.FieldValues.Add(fieldValue);
        // PK store-generated con Id asignado: marcar Added explícito para forzar INSERT (ver UpsertHydratedFields).
        repo.Add(fieldValue);
    }

    /// <summary>
    /// Persiste (upsert por field_key) los atributos del vehículo que el proveedor RUNT extrajo
    /// en la misma consulta del preflight, con Source="consultation". Reusa la convención de
    /// valores "loose" (FormFieldId null) de <c>RunConsultationHandler</c>. Idempotente.
    /// </summary>
    /// <summary>
    /// Cuando el organismo lo elige el operador, el que reporta el RUNT es el organismo ACTUAL del
    /// vehículo, no el destino del trámite: se reetiqueta a <c>transit_office_actual_*</c> antes de
    /// persistirlo.
    ///
    /// <para>Sin esto, cada consulta al RUNT sobrescribía con el organismo de origen la secretaría de
    /// destino que el gestor acababa de elegir — y con ella el grant, la bandeja y quién aprueba el
    /// trámite. En los tipos cuyo organismo impone el RUNT no se toca nada: la lista pasa tal cual.</para>
    /// </summary>
    private static IReadOnlyList<HydratedField> RedirigirOrganismoDelRunt(
        ProcedureInstance instance,
        IReadOnlyList<HydratedField> vehicleFields)
    {
        var eligeElOperador = ProcedureTypeGateProfile
            .FromJson(instance.ProcedureType?.GateProfile)
            .OperatorChoosesTransitOffice();
        if (!eligeElOperador || vehicleFields.Count == 0)
            return vehicleFields;

        return [.. vehicleFields.Select(f => RenombrarOrganismo(f.FieldKey) is { } destino
            ? f with { FieldKey = destino }
            : f)];
    }

    /// <summary>Clave descriptiva equivalente, o <c>null</c> si el campo no es del organismo.</summary>
    private static string? RenombrarOrganismo(string fieldKey) =>
        fieldKey.ToLowerInvariant() switch
        {
            TransitOfficeFieldKeys.Id => TransitOfficeFieldKeys.ActualId,
            TransitOfficeFieldKeys.Code => TransitOfficeFieldKeys.ActualCode,
            TransitOfficeFieldKeys.Name => TransitOfficeFieldKeys.ActualName,
            TransitOfficeFieldKeys.City => TransitOfficeFieldKeys.ActualCity,
            _ => null,
        };

    private void UpsertHydratedFields(
        ProcedureInstance instance,
        Guid tenantId,
        IReadOnlyList<HydratedField> hydratedFields)
    {
        if (hydratedFields.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;

        foreach (var field in hydratedFields)
        {
            // A4/B4 (HU #10673) — color/combustible admiten transformación declarada: el snapshot RUNT
            // siempre se refresca, pero el valor efectivo NO se pisa si hay un cambio activo (ver ADR-0029).
            if (TransformationFlagByEffectiveKey.TryGetValue(field.FieldKey, out var flagKey))
            {
                UpsertTransformationAwareField(instance, tenantId, field, flagKey, now);
                continue;
            }

            UpsertSingleField(instance, tenantId, field.FieldKey, field.ValueText, field.ValueJson, now);
        }
    }

    /// <summary>
    /// Upsert idempotente por field_key de un único valor "loose" (FormFieldId null, Source="consultation").
    /// Mismo idiom de INSERT explícito (PK store-generated) que el resto del handler.
    /// </summary>
    private void UpsertSingleField(
        ProcedureInstance instance,
        Guid tenantId,
        string fieldKey,
        string? valueText,
        string? valueJson,
        DateTimeOffset now)
    {
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == fieldKey);
        if (existing is not null)
        {
            existing.ValueText = valueText;
            existing.ValueJson = valueJson;
            existing.Source = ConsultationSource;
            existing.UpdatedAt = now;
        }
        else
        {
            var fieldValue = new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instance.Id,
                FormFieldId = null,
                FieldKey = fieldKey,
                ValueText = valueText,
                ValueJson = valueJson,
                Source = ConsultationSource,
                CreatedAt = now,
            };
            instance.FieldValues.Add(fieldValue);
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
            // INSERT (sin esto EF infiere Modified por la PK no-default → UPDATE de 0 filas).
            repo.Add(fieldValue);
        }
    }

    /// <summary>
    /// A4/B4 (HU #10673, ADR-0029) — upsert del atributo transformable (color/combustible). El snapshot RUNT
    /// (<c>{key}_runt</c>) SIEMPRE se refresca con el valor recién consultado. El valor EFECTIVO (el que va al
    /// FUR) solo se sobrescribe si NO hay una transformación activa: sin esto, cada re-consulta RUNT (botón
    /// "Actualizar") pisaría el cambio de color/combustible declarado por el operador — el problema técnico
    /// central del ADR. "Activa" = flag <c>cambio_*</c> = "true" o el efectivo ya difiere del snapshot previo.
    /// </summary>
    private void UpsertTransformationAwareField(
        ProcedureInstance instance,
        Guid tenantId,
        HydratedField field,
        string flagKey,
        DateTimeOffset now)
    {
        var snapshotKey = field.FieldKey + RuntSnapshotSuffix;
        var previousSnapshot = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, snapshotKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;

        // El snapshot RUNT siempre refleja la última consulta.
        UpsertSingleField(instance, tenantId, snapshotKey, field.ValueText, field.ValueJson, now);

        // Nunca pisar un cambio declarado: si hay transformación activa, el efectivo se conserva.
        if (HasActiveTransformation(instance, field.FieldKey, flagKey, previousSnapshot))
            return;

        UpsertSingleField(instance, tenantId, field.FieldKey, field.ValueText, field.ValueJson, now);
    }

    /// <summary>
    /// Determina si hay una transformación de color/combustible ACTIVA que el preflight no debe pisar:
    /// (a) flag explícito <c>cambio_*</c> = "true" (puesto por el operador vía PATCH field-values), o
    /// (b) cambio implícito: el efectivo ya difiere del snapshot RUNT previo (declaración sin flag).
    /// </summary>
    private static bool HasActiveTransformation(
        ProcedureInstance instance,
        string effectiveKey,
        string flagKey,
        string? previousSnapshot)
    {
        var flag = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, flagKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        if (string.Equals(flag?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            return true;

        var effective = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, effectiveKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        return !string.IsNullOrWhiteSpace(effective)
            && !string.IsNullOrWhiteSpace(previousSnapshot)
            && !string.Equals(effective.Trim(), previousSnapshot.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// B11 (HU #10659) — cuando el vehículo YA está inscrito, el organismo lo fija el RUNT y no el
    /// operador: tras hidratar <c>transit_office_name</c>, resuelve el OT habilitado de la empresa
    /// (por nombre, case-insensitive) y fija también <c>transit_office_id</c>,
    /// <c>transit_office_code</c> y <c>transit_office_city</c> (Source="consultation"). Si el nombre
    /// RUNT NO coincide con ningún OT habilitado se conserva solo <c>transit_office_name</c> y NO se
    /// inventa un id (el trámite queda a la espera de que el SuperAdmin habilite el grant correcto).
    ///
    /// <para>Aplica a traspaso_standard —donde nació— y a TODA la familia OTROS: un cambio de color o
    /// un levantamiento de prenda se radican, por norma, ante el organismo donde el vehículo está
    /// matriculado. Estaba atado al código de una sola tipología, así que la familia OTROS dejaba el
    /// organismo sin resolver y el gestor lo elegía a mano, pudiendo escoger uno distinto al del RUNT.
    /// En matrícula inicial no hace nada: allí no hay inscripción previa y el operador sí elige.</para>
    /// </summary>
    private async Task AutoBindTransitOfficeFromRuntAsync(
        ProcedureInstance instance,
        Guid tenantId,
        CancellationToken ct)
    {
        var esTraspasoStandard = string.Equals(
            instance.TypeCode, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.Ordinal);
        if (!esTraspasoStandard && instance.Family != ProcedureFamily.Otros)
            return;

        // Cuando el organismo lo ELIGE el operador, el del RUNT no es el destino del trámite: es
        // dónde está el vehículo hoy. Escribirlo en las claves canónicas pisaría en cada consulta la
        // secretaría de destino que el gestor acaba de escoger —y con ella el grant, la bandeja y
        // quién aprueba—, así que va a las claves descriptivas.
        var eligeElOperador = ProcedureTypeGateProfile
            .FromJson(instance.ProcedureType?.GateProfile)
            .OperatorChoosesTransitOffice();

        var runtName = instance.FieldValues
            .FirstOrDefault(f => string.Equals(
                f.FieldKey,
                eligeElOperador ? TransitOfficeFieldKeys.ActualName : TransitOfficeFieldKeys.Name,
                StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        if (string.IsNullOrWhiteSpace(runtName))
            return;

        var match = await transitOfficeResolver.ResolveEnabledByNameAsync(tenantId, runtName, ct);
        if (match is null)
            return; // Sin OT habilitado que coincida: se deja solo el nombre RUNT (no se inventa id).

        // Reusa el upsert idempotente de campos hidratados (Source="consultation").
        UpsertHydratedFields(instance, tenantId, eligeElOperador
            ?
            [
                new HydratedField(TransitOfficeFieldKeys.ActualId, match.Id.ToString(), null),
                new HydratedField(TransitOfficeFieldKeys.ActualCode, match.Code, null),
                new HydratedField(TransitOfficeFieldKeys.ActualName, match.Name, null),
                new HydratedField(TransitOfficeFieldKeys.ActualCity, match.CityCode, null),
            ]
            :
            [
                new HydratedField(TransitOfficeFieldKeys.Id, match.Id.ToString(), null),
                new HydratedField(TransitOfficeFieldKeys.Code, match.Code, null),
                new HydratedField(TransitOfficeFieldKeys.Name, match.Name, null),
                new HydratedField(TransitOfficeFieldKeys.City, match.CityCode, null),
            ]);
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    private static ActorRef? ActorOf(ProcedureInstance instance, string actorType)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
        if (a is null || string.IsNullOrWhiteSpace(a.DocumentType) || string.IsNullOrWhiteSpace(a.DocumentNumber))
            return null;
        return new ActorRef(a.DocumentType, a.DocumentNumber, a.PersonType);
    }

    // OT destino elegido en el wizard (transit_office_id en field_values). null si aún no se ha elegido;
    // en ese caso IRnmcRequirementPolicy cae al único grant vigente o al default seguro (false).
    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    internal sealed record ActorRef(string DocumentType, string DocumentNumber, string? PersonType);
}

/// <summary>GET del último snapshot de preflight. Devuelve null si aún no se ha corrido.</summary>
public sealed class GetPreflightHandler(IProcedureInstanceRepository repo)
{
    public async Task<(PreflightSnapshotDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var snapshot = await repo.GetLatestPreflightAsync(id, tenantId, ct);
        if (snapshot is null)
            return (null, null); // 200 con null (contrato: "...| null").

        var checks = DeserializeChecks(snapshot.Checks);
        return (new PreflightSnapshotDto(snapshot.Overall, checks, snapshot.Provider, snapshot.CreatedAt), null);
    }

    internal static IReadOnlyList<PreflightCheckDto> DeserializeChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PreflightCheckDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
