using System.Text.Json;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// FEATURE 05 — Consulta RNMC (medidas correctivas de la Policía) DESACOPLADA del pre-vuelo. Corre por
/// cada actor persona natural (comprador; + vendedor en traspaso) usando su fecha de expedición
/// (<c>{rol}_document_issue_date</c>), solo cuando el RNMC aplica (opt-in de la compañía para el OT, o el
/// requisito del OT como fallback). Se dispara en el paso final del wizard —cuando la fecha ya está
/// capturada—, no en el pre-vuelo (donde aún no hay fecha).
///
/// Persiste el resultado como el field_value <c>rnmc_checks</c> (<c>ValueJson</c> = lista JSON de
/// <see cref="PreflightCheckDto"/>, Source="system"), del que leen el paso final y el certificado del FUR.
/// También refresca la señal informativa <c>rnmc_medida_pendiente</c> para visibilidad del OT.
/// </summary>
public sealed class RunRnmcConsultHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry,
    IRnmcRequirementPolicy rnmcPolicy,
    IConsultationRestrictionPolicy restrictionPolicy)
{
    private const string ProviderVerifikRnmc = "verifik_rnmc";
    private const string SystemSource = "system";
    public const string FieldRnmcChecks = "rnmc_checks";
    private const string FieldRnmcMedidaPendiente = "rnmc_medida_pendiente";

    public async Task<(IReadOnlyList<PreflightCheckDto>? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada);
        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var otId = TransitOfficeIdFromFieldValues(instance);

        // ¿Aplica el RNMC? Opt-in de la compañía para el OT; si no hay decisión explícita, el requisito
        // del OT (default false). Misma condición que gobierna la visibilidad de la fecha de expedición.
        var rnmcRequired = await rnmcPolicy.IsRnmcRequiredAsync(tenantId, otId, ct);
        var restrictions = await restrictionPolicy.GetAsync(tenantId, otId, ct);
        var rnmcApplies = restrictions.SettingOf(ConsultationRestrictionKinds.Rnmc) ?? rnmcRequired;

        var checks = new List<PreflightCheckDto>();
        if (rnmcApplies)
        {
            var comprador = ActorOf(instance, "comprador");
            await RunRnmcForActorAsync(checks, "comprador", comprador, fieldValues, ct);

            if (modalidad == TramiteModalidadEntrada.Traspaso)
            {
                var vendedor = ActorOf(instance, "vendedor");
                await RunRnmcForActorAsync(checks, "vendedor", vendedor, fieldValues, ct);
            }
        }

        // Persistir el resultado (aunque sea vacío, para limpiar una consulta previa obsoleta). Se
        // serializa con las opciones por defecto para casar con GetPreflightHandler.DeserializeChecks.
        SetFieldJson(instance, tenantId, FieldRnmcChecks, JsonSerializer.Serialize(checks));

        // Señal informativa "medida correctiva pendiente" (warn) para visibilidad del OT.
        var hasMedida = checks.Any(c =>
            c.Key.EndsWith("medidas_correctivas", StringComparison.Ordinal) && c.Status == "warn");
        SetSignal(instance, tenantId, FieldRnmcMedidaPendiente, hasMedida ? "true" : "false");

        await repo.SaveChangesAsync(ct);
        return (checks, null);
    }

    /// <summary>
    /// Consulta el RNMC de un actor persona natural con su fecha de expedición y agrega los checks con
    /// prefijo <c>rnmc_{rol}</c>. No hace nada si el actor falta o es jurídico (NIT).
    /// </summary>
    private async Task RunRnmcForActorAsync(
        List<PreflightCheckDto> checks,
        string role,
        ActorRef? actor,
        Dictionary<string, string?> fieldValues,
        CancellationToken ct)
    {
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

        var fv = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["owner_document_type"] = actor.DocumentType,
            ["owner_document_number"] = actor.DocumentNumber,
            ["document_issue_date"] = Get(fieldValues, $"{role}_document_issue_date") ?? Get(fieldValues, "document_issue_date"),
        };

        try
        {
            var ctx = new ConsultationContext(Guid.Empty, Guid.Empty, provider.Key, fv);
            var result = await provider.ConsultAsync(ctx, ct);
            foreach (var c in result.Checks)
                checks.Add(new PreflightCheckDto($"{keyPrefix}_{c.Key}", c.Label, c.Status, c.Source, c.Message));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            checks.Add(new PreflightCheckDto(keyPrefix, $"Consulta RNMC ({role})", "error", ProviderVerifikRnmc,
                "No fue posible verificar la información en el RNMC en este momento. Vuelve a intentarlo en unos minutos."));
        }
    }

    private static bool IsNaturalPerson(ActorRef actor) =>
        ActorPersonTypes.IsNatural(actor.PersonType)
        || (string.IsNullOrWhiteSpace(actor.PersonType)
            && !string.Equals(actor.DocumentType, "NIT", StringComparison.OrdinalIgnoreCase));

    private void SetFieldJson(ProcedureInstance instance, Guid tenantId, string fieldKey, string json)
    {
        var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == fieldKey);
        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            existing.ValueText = null;
            existing.ValueJson = json;
            existing.Source = SystemSource;
            existing.UpdatedAt = now;
            return;
        }

        var fv = new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = instance.Id,
            FormFieldId = null,
            FieldKey = fieldKey,
            ValueText = null,
            ValueJson = json,
            Source = SystemSource,
            CreatedAt = now,
        };
        instance.FieldValues.Add(fv);
        repo.Add(fv);
    }

    private void SetSignal(ProcedureInstance instance, Guid tenantId, string fieldKey, string value)
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

        // Solo creamos la señal cuando hay medida (value="true"); "false" sin fila previa es no-op.
        if (value != "true")
            return;

        var fv = new ProcedureInstanceFieldValue
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
        instance.FieldValues.Add(fv);
        repo.Add(fv);
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static ActorRef? ActorOf(ProcedureInstance instance, string actorType)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
        if (a is null || string.IsNullOrWhiteSpace(a.DocumentType) || string.IsNullOrWhiteSpace(a.DocumentNumber))
            return null;
        return new ActorRef(a.DocumentType, a.DocumentNumber, a.PersonType);
    }

    private sealed record ActorRef(string DocumentType, string DocumentNumber, string? PersonType);
}

/// <summary>
/// GET del último resultado RNMC persistido (field_value <c>rnmc_checks</c>). Lista vacía si aún no se
/// consultó. No dispara la consulta (eso lo hace <see cref="RunRnmcConsultHandler"/>).
/// </summary>
public sealed class GetRnmcHandler(IProcedureInstanceRepository repo)
{
    public async Task<(IReadOnlyList<PreflightCheckDto>? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var json = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, RunRnmcConsultHandler.FieldRnmcChecks, StringComparison.Ordinal))
            ?.ValueJson;

        return (GetPreflightHandler.DeserializeChecks(json), null);
    }
}
