using System.Globalization;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Un check del semáforo preflight, en la forma congelada del contrato.</summary>
public sealed record PreflightCheckDto(
    string Key,
    string Label,
    string Status,
    string Source,
    string? Message);

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
    IRnmcRequirementPolicy rnmcPolicy)
{
    private const string ProviderVerifikSimit = "verifik_simit";
    private const string ProviderVerifikRnmc = "verifik_rnmc";
    private const string ConsultationSource = "consultation";
    private const string SystemSource = "system";
    private const string FieldVinConflictoTraspaso = "vin_conflicto_traspaso";
    private const string CheckVinMatricula = "vin_matricula";
    private const string FieldRnmcMedidaPendiente = "rnmc_medida_pendiente";

    public async Task<(PreflightSnapshotDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != TramiteEstado.Borrador)
            return (null, "not_draft");

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada);

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var vin = Get(fieldValues, "vin");
        var plate = Get(fieldValues, "plate");
        var comprador = ActorOf(instance, "comprador");
        var vendedor = ActorOf(instance, "vendedor");

        var checks = new List<PreflightCheckDto>();
        var providersUsed = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlyList<HydratedField> vehicleFields;

        // HU #10478: la cadena de proveedores (Kyverum-first → Verifik) y el presupuesto de failover
        // salen de la config del tenant; null ⇒ defaults globales.
        var tenantOverride = await overrideProvider.GetAsync(tenantId, ct);

        // HU #10602 (R18) — RNMC condicionado por el OT destino (admin.ot_requirements.requires_rnmc)
        // y SOLO para actores persona natural (comprador/vendedor). El jurídico (NIT) nunca consulta RNMC.
        var requiresRnmc = await rnmcPolicy.IsRnmcRequiredAsync(
            tenantId, TransitOfficeIdFromFieldValues(instance), ct);

        if (modalidad == TramiteModalidadEntrada.Traspaso)
        {
            // Vehículo por placa (requiere documento del propietario actual). El doc del
            // propietario se persiste en field_values en el paso "consulta" (puede llegar
            // antes de que exista el actor vendedor); de ahí lo toma el provider.
            vehicleFields = await RunVehiculoAsync(checks, providersUsed, ConsultationKind.VehiclePlate, instance.Id, tenantId, vin, plate, fieldValues, tenantOverride, ct);
            // SIMIT del comprador y del vendedor (comparendos).
            await RunSimitAsync(checks, providersUsed, "simit_comprador", "SIMIT comprador", comprador, ct);
            await RunSimitAsync(checks, providersUsed, "simit_vendedor", "SIMIT vendedor", vendedor, ct);
            // RNMC (medidas correctivas) por cada actor persona natural, si el OT lo exige.
            if (requiresRnmc)
            {
                await RunRnmcAsync(checks, providersUsed, "comprador", comprador, fieldValues, ct);
                await RunRnmcAsync(checks, providersUsed, "vendedor", vendedor, fieldValues, ct);
            }
        }
        else
        {
            // Matrícula inicial: vehículo por VIN (primera matrícula, sin propietario previo).
            vehicleFields = await RunVehiculoAsync(checks, providersUsed, ConsultationKind.VehicleVin, instance.Id, tenantId, vin, plate, fieldValues, tenantOverride, ct);
            // R3 (HU #10538): si el VIN ya tiene una matrícula previa en el tenant, informar y ofrecer traspaso.
            await DetectVinConflictoTraspasoAsync(checks, instance, tenantId, vin, ct);
            // RNMC del comprador/propietario persona natural, si el OT lo exige (ambas modalidades, R18).
            if (requiresRnmc)
            {
                await RunRnmcAsync(checks, providersUsed, "comprador", comprador, fieldValues, ct);
            }
        }

        // HU #10604 (R19) — señal server-driven de medida correctiva RNMC pendiente ("Imponer Medida")
        // para el gate de envío al OT (EvaluarEntregaAsync). Se pone en true si algún check RNMC quedó
        // en "fail" (medidas correctivas); si no, se baja a false (sin crear filas innecesarias).
        var hasRnmcMedida = checks.Any(c =>
            c.Key.StartsWith("rnmc_", StringComparison.Ordinal) && c.Status == "fail");
        if (hasRnmcMedida)
            SetSignalIfChanged(instance, tenantId, FieldRnmcMedidaPendiente, "true", createIfMissing: true);
        else
            SetSignalIfChanged(instance, tenantId, FieldRnmcMedidaPendiente, "false", createIfMissing: false);

        // Una sola consulta a Verifik alimenta AMBAS secciones: el proveedor del vehículo ya
        // devolvió los atributos del RUNT (marca/línea/color/…). Los persistimos en field_values
        // (source="consultation") para la tarjeta "Datos del vehículo", evitando una segunda
        // consulta dedicada. Idempotente: upsert por field_key.
        UpsertHydratedFields(instance, tenantId, vehicleFields);

        // Matrícula inicial: el estado del vehículo no debe bloquear (ver RelajarEstadoVehiculoMatricula).
        RelajarEstadoVehiculoMatricula(checks, modalidad);

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

        return (new PreflightSnapshotDto(overall, checks, provider, now), null);
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
    /// En MATRÍCULA INICIAL el estado del vehículo en RUNT suele ser <c>"REGISTRADO"</c> (registrado
    /// pero aún no activo/matriculado), no <c>"ACTIVO"</c>: es el estado ESPERADO para un 0 km, así que
    /// NO debe bloquear. Se degrada el check <c>estado_vehiculo</c> de <c>fail</c> a <c>warn</c>
    /// (informativo, amarillo) para que nunca pinte el preflight en rojo. En traspaso se exige
    /// <c>"ACTIVO"</c> (vehículo en circulación) → el <c>fail</c> se mantiene intacto.
    /// </summary>
    private static void RelajarEstadoVehiculoMatricula(
        List<PreflightCheckDto> checks,
        TramiteModalidadEntrada? modalidad)
    {
        if (modalidad != TramiteModalidadEntrada.MatriculaInicial)
            return;

        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            if (string.Equals(c.Key, "estado_vehiculo", StringComparison.Ordinal) && c.Status == "fail")
                checks[i] = c with { Status = "warn" };
        }
    }

    /// <summary>
    /// Corre la consulta de vehículo a través de la CADENA de proveedores (HU #10478): Kyverum RUNT
    /// primero, Verifik como fallback, según la config del tenant. El provider concreto decide VIN vs
    /// placa desde los field_values (VIN prioritario); <paramref name="kind"/> selecciona la cadena por
    /// modalidad. Nunca propaga 500: cualquier excepción inesperada se traduce a un check <c>error</c>.
    /// </summary>
    private async Task<IReadOnlyList<HydratedField>> RunVehiculoAsync(
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        ConsultationKind kind,
        Guid instanceId,
        Guid tenantId,
        string? vin,
        string? plate,
        Dictionary<string, string?> fieldValues,
        ConsultationTenantOverride? tenantOverride,
        CancellationToken ct)
    {
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
                checks.Add(new PreflightCheckDto(c.Key, c.Label, c.Status, c.Source, c.Message));

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

    private async Task RunSimitAsync(
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        string fallbackKey,
        string fallbackLabel,
        ActorRef? actor,
        CancellationToken ct)
    {
        var provider = registry.Resolve(ProviderVerifikSimit);
        if (provider is null)
        {
            checks.Add(new PreflightCheckDto(fallbackKey, fallbackLabel, "error", ProviderVerifikSimit,
                "No fue posible verificar la información en SIMIT en este momento. Vuelve a intentarlo en unos minutos."));
            return;
        }

        if (actor is null)
        {
            checks.Add(new PreflightCheckDto(fallbackKey, fallbackLabel, "unknown", ProviderVerifikSimit, "Actor sin documento para consultar SIMIT"));
            return;
        }

        providersUsed.Add(ProviderVerifikSimit);
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = actor.DocumentType,
            ["owner_document_number"] = actor.DocumentNumber,
        };

        await RunProviderAsync(checks, provider, fv, ct, keyPrefix: fallbackKey);
    }

    private async Task RunRnmcAsync(
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        string role,
        ActorRef? actor,
        Dictionary<string, string?> fieldValues,
        CancellationToken ct)
    {
        // RNMC solo aplica a personas naturales: sin actor, o si el actor es jurídico (NIT), no se consulta.
        if (actor is null || !IsNaturalPerson(actor))
            return;

        var keyPrefix = $"rnmc_{role}";
        var provider = registry.Resolve(ProviderVerifikRnmc);
        if (provider is null)
        {
            checks.Add(new PreflightCheckDto(keyPrefix, $"Consulta RNMC ({role})", "error", ProviderVerifikRnmc,
                "No fue posible verificar la información en el RNMC en este momento. Vuelve a intentarlo en unos minutos."));
            return;
        }

        providersUsed.Add(ProviderVerifikRnmc);
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = actor.DocumentType,
            ["owner_document_number"] = actor.DocumentNumber,
            // Fecha de expedición que el RNMC real exige: por actor (rol) o genérica; si falta, el
            // provider real la trata como dato ausente (unknown, no bloquea).
            ["document_issue_date"] = Get(fieldValues, $"{role}_document_issue_date") ?? Get(fieldValues, "document_issue_date"),
        };

        await RunProviderAsync(checks, provider, fv, ct, keyPrefix: keyPrefix);
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
                checks.Add(new PreflightCheckDto(key, c.Label, c.Status, c.Source, c.Message));
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
    /// R3 (HU #10538) — en MATRÍCULA INICIAL detecta si el VIN ya tiene una matrícula previa en el mismo
    /// tenant (invariante <see cref="VinPolicyEvaluator"/>). Si hay conflicto: agrega el check informativo
    /// <see cref="CheckVinMatricula"/> (<c>warn</c>, con secretaría + fecha del registro previo) e hidrata
    /// la señal <see cref="FieldVinConflictoTraspaso"/> = <c>true</c> en field_values para que el front
    /// ofrezca la ruta de traspaso. Sin previas o con la única previa en estado <c>rechazado</c> no hay
    /// conflicto (AC2/AC3): en ese caso la señal previa se limpia (idempotencia entre corridas si el
    /// gestor cambió el VIN). El estado <c>warn</c> mantiene el preflight informativo (nunca bloquea rojo).
    /// </summary>
    private async Task DetectVinConflictoTraspasoAsync(
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
            return;
        }

        var previo = conflicto.ExistingTramite;
        var secretaria = string.IsNullOrWhiteSpace(previo.Secretaria) ? "otra secretaría" : previo.Secretaria!;
        var fecha = previo.FechaRegistro?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cuando = fecha is null ? string.Empty : $" el {fecha}";

        checks.Add(new PreflightCheckDto(
            CheckVinMatricula,
            "VIN ya matriculado",
            "warn",
            SystemSource,
            $"Este VIN ya tiene una matrícula registrada en {secretaria}{cuando}. " +
            "Puede iniciar un traspaso de este vehículo en lugar de una nueva matrícula."));

        SetSignalIfChanged(instance, tenantId, FieldVinConflictoTraspaso, "true", createIfMissing: true);
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
            var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == field.FieldKey);
            if (existing is not null)
            {
                existing.ValueText = field.ValueText;
                existing.ValueJson = field.ValueJson;
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
                    FieldKey = field.FieldKey,
                    ValueText = field.ValueText,
                    ValueJson = field.ValueJson,
                    Source = ConsultationSource,
                    CreatedAt = now,
                };
                instance.FieldValues.Add(fieldValue);
                // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
                // INSERT (sin esto EF infiere Modified por la PK no-default → UPDATE de 0 filas).
                repo.Add(fieldValue);
            }
        }
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

    private sealed record ActorRef(string DocumentType, string DocumentNumber, string? PersonType);
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
