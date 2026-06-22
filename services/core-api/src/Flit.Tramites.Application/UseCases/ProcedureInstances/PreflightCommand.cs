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

        if (modalidad == TramiteModalidadEntrada.Traspaso)
        {
            // Vehículo por placa (requiere documento del propietario actual). El doc del
            // propietario se persiste en field_values en el paso "consulta" (puede llegar
            // antes de que exista el actor vendedor); de ahí lo toma el provider.
            await RunVehiculoAsync(checks, providersUsed, vin, plate, fieldValues, ct);
            // SIMIT del comprador y del vendedor (comparendos).
            await RunSimitAsync(checks, providersUsed, "simit_comprador", "SIMIT comprador", comprador, ct);
            await RunSimitAsync(checks, providersUsed, "simit_vendedor", "SIMIT vendedor", vendedor, ct);
            // RNMC del comprador (medidas correctivas).
            await RunRnmcAsync(checks, providersUsed, comprador, ct);
        }
        else
        {
            // Matrícula inicial: vehículo por VIN (primera matrícula, sin propietario previo).
            await RunVehiculoAsync(checks, providersUsed, vin, plate, fieldValues, ct);
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

        return (new PreflightSnapshotDto(overall, checks, provider, now), null);
    }

    /// <summary>
    /// Regla del dominio: cualquier <c>fail</c> → red; algún <c>warn</c> → yellow; resto → green.
    /// <c>unknown</c> NO impide green (provider degradado no bloquea). Sin checks → green.
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

    private async Task RunVehiculoAsync(
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
            checks.Add(new PreflightCheckDto("vehiculo", "Vehículo RUNT", "unknown", ProviderVerifik, "Proveedor de vehículo no disponible"));
            return;
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

        await RunProviderAsync(checks, provider, fv, ct);
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
            checks.Add(new PreflightCheckDto(fallbackKey, fallbackLabel, "unknown", ProviderVerifikSimit, "Proveedor SIMIT no disponible"));
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
            checks.Add(new PreflightCheckDto("rnmc", "RNMC comprador", "unknown", ProviderVerifikRnmc, "Proveedor RNMC no disponible"));
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
    /// Cualquier excepción inesperada degrada a un check unknown (no propaga 500).
    /// </summary>
    private static async Task RunProviderAsync(
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
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var key = keyPrefix ?? "provider";
            checks.Add(new PreflightCheckDto(key, provider.Key, "unknown", provider.Key, $"Error inesperado: {ex.Message}"));
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
