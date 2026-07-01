using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;

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
    IConsultationProviderRegistry registry)
{
    private const string ProviderVerifik = "verifik";
    private const string ProviderVerifikSimit = "verifik_simit";
    private const string ProviderVerifikRnmc = "verifik_rnmc";
    private const string ConsultationSource = "consultation";

    public async Task<(PreflightSnapshotDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
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

        if (modalidad == TramiteModalidadEntrada.Traspaso)
        {
            // Vehículo por placa (requiere documento del propietario actual). El doc del
            // propietario se persiste en field_values en el paso "consulta" (puede llegar
            // antes de que exista el actor vendedor); de ahí lo toma el provider.
            vehicleFields = await RunVehiculoAsync(checks, providersUsed, vin, plate, fieldValues, ct);
            // SIMIT del comprador y del vendedor (comparendos).
            await RunSimitAsync(checks, providersUsed, "simit_comprador", "SIMIT comprador", comprador, ct);
            await RunSimitAsync(checks, providersUsed, "simit_vendedor", "SIMIT vendedor", vendedor, ct);
            // RNMC del comprador (medidas correctivas).
            await RunRnmcAsync(checks, providersUsed, comprador, ct);
        }
        else
        {
            // Matrícula inicial: vehículo por VIN (primera matrícula, sin propietario previo).
            vehicleFields = await RunVehiculoAsync(checks, providersUsed, vin, plate, fieldValues, ct);
        }

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

    private async Task<IReadOnlyList<HydratedField>> RunVehiculoAsync(
        List<PreflightCheckDto> checks,
        SortedSet<string> providersUsed,
        string? vin,
        string? plate,
        Dictionary<string, string?> fieldValues,
        CancellationToken ct)
    {
        var provider = registry.Resolve(ProviderVerifik);
        if (provider is null)
        {
            checks.Add(new PreflightCheckDto("vehiculo", "Consulta de vehículo", "error", ProviderVerifik,
                "No fue posible verificar la información del vehículo en el RUNT en este momento. Vuelve a intentarlo en unos minutos."));
            return [];
        }

        providersUsed.Add(ProviderVerifik);
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(vin)) fv["vin"] = vin;
        if (!string.IsNullOrWhiteSpace(plate)) fv["plate"] = plate;
        // Documento del propietario para la consulta por placa: lo lee el provider
        // vehicle-by-plate (D-201-1) de los field_values, donde lo persiste el paso "consulta".
        var ownerDocType = Get(fieldValues, "owner_document_type");
        var ownerDocNumber = Get(fieldValues, "owner_document_number");
        if (!string.IsNullOrWhiteSpace(ownerDocType)) fv["owner_document_type"] = ownerDocType;
        if (!string.IsNullOrWhiteSpace(ownerDocNumber)) fv["owner_document_number"] = ownerDocNumber;

        return await RunProviderAsync(checks, provider, fv, ct);
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
        ActorRef? actor,
        CancellationToken ct)
    {
        var provider = registry.Resolve(ProviderVerifikRnmc);
        if (provider is null)
        {
            checks.Add(new PreflightCheckDto("rnmc", "Consulta RNMC (Policía)", "error", ProviderVerifikRnmc,
                "No fue posible verificar la información en el RNMC en este momento. Vuelve a intentarlo en unos minutos."));
            return;
        }

        if (actor is null)
        {
            checks.Add(new PreflightCheckDto("rnmc", "RNMC comprador", "unknown", ProviderVerifikRnmc, "Comprador sin documento para consultar RNMC"));
            return;
        }

        providersUsed.Add(ProviderVerifikRnmc);
        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = actor.DocumentType,
            ["owner_document_number"] = actor.DocumentNumber,
        };

        await RunProviderAsync(checks, provider, fv, ct, keyPrefix: "rnmc");
    }

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
        return new ActorRef(a.DocumentType, a.DocumentNumber);
    }

    private sealed record ActorRef(string DocumentType, string DocumentNumber);
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
