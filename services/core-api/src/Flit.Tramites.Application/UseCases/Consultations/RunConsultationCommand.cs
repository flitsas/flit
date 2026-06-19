using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Ejecuta una consulta multi-proveedor sobre una instancia de trámite:
/// resuelve el proveedor desde el template, llama al provider y persiste los
/// HydratedFields en field_values con Source="consultation".
/// </summary>
public sealed class RunConsultationHandler(
    IProcedureInstanceRepository instanceRepo,
    ICatalogRepository catalogRepo,
    IConsultationProviderRegistry registry)
{
    private const string ConsultationSource = "consultation";

    public async Task<(ConsultationResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string templateCode,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var template = await catalogRepo.GetConsultationTemplateByCodeAsync(templateCode, ct);
        if (template is null)
            return (null, "template_not_found");

        var providerKey = ResolveProviderKey(template.ExternalRefs);
        if (string.IsNullOrWhiteSpace(providerKey))
            return (null, "provider_not_resolved");

        var provider = registry.Resolve(providerKey);
        if (provider is null)
            return (null, "provider_not_found");

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var ctx = new ConsultationContext(
            instance.Id,
            instance.TenantId,
            templateCode,
            fieldValues);

        var result = await provider.ConsultAsync(ctx, ct);

        UpsertHydratedFields(instance, tenantId, instanceRepo, result.HydratedFields);

        try
        {
            await instanceRepo.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsNotDraftViolation(ex))
        {
            // AC2: trigger DB bloquea escritura de field_values si la instancia no
            // está en draft (check_violation). Mapeamos a un error de dominio.
            return (null, "not_draft");
        }

        return (result, null);
    }

    private static string? ResolveProviderKey(string externalRefsJson)
    {
        if (string.IsNullOrWhiteSpace(externalRefsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(externalRefsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("provider", out var providerEl) &&
                providerEl.ValueKind == JsonValueKind.String)
            {
                return providerEl.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static void UpsertHydratedFields(
        ProcedureInstance instance,
        Guid tenantId,
        IProcedureInstanceRepository repo,
        IReadOnlyList<HydratedField> hydratedFields)
    {
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
                    FormFieldId = Guid.Empty,
                    FieldKey = field.FieldKey,
                    ValueText = field.ValueText,
                    ValueJson = field.ValueJson,
                    Source = ConsultationSource,
                    CreatedAt = now
                };
                instance.FieldValues.Add(fieldValue);
                // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
                // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
                repo.Add(fieldValue);
            }
        }
    }

    private static bool IsNotDraftViolation(Exception ex)
    {
        // Application no referencia EF/Npgsql: detectamos el check_violation del
        // trigger por el texto del mensaje en toda la cadena de excepciones.
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message;
            if (msg.Contains("check_violation", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("draft", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
