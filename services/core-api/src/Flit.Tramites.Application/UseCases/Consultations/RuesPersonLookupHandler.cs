using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona JURÍDICA en RUES por NIT, para autopoblar la razón social del
/// actor jurídico en el wizard (bifurcación del "Consultar RUNT" cuando personType=juridical).
/// Delega en el provider <c>verifik_rues</c> y, si la instancia está en borrador, PERSISTE los campos
/// RUES en <c>field_values</c> (Source="consultation", HU #10856) para que el certificado RUES los
/// muestre — igual que hace el RUNT. Fuera de draft no persiste (el autopoblado sigue funcionando).
/// Es el análogo jurídico de <see cref="RuntPersonLookupHandler"/> (conductor / persona natural).
/// </summary>
public sealed class RuesPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry)
{
    private const string ConsultationSource = "consultation";

    private const string RuesProviderKey = "verifik_rues";

    public async Task<(RuesPersonDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string? documentNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            return (null, "invalid_request");

        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var provider = registry.Resolve(RuesProviderKey);
        if (provider is null)
            return (null, "provider_not_found");

        var nit = documentNumber.Trim();
        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nit"] = nit,
            ["documentNumber"] = nit,
        };

        var ctx = new ConsultationContext(instanceId, tenantId, "RUES_ACTOR_JURIDICAL", fieldValues);
        var result = await provider.ConsultAsync(ctx, ct);

        // HU #10856 — persistir los campos RUES en field_values (como el RUNT) para que el certificado
        // los muestre. Solo en borrador: fuera de draft el trigger de la BD bloquea la escritura, así
        // que se omite (el autopoblado del actor sigue funcionando).
        if (string.Equals(instance.Status, TramiteEstado.Borrador, StringComparison.OrdinalIgnoreCase)
            && result.HydratedFields.Count > 0)
        {
            UpsertHydrated(instance, tenantId, result.HydratedFields);
            await repo.SaveChangesAsync(ct);
        }

        var razonSocial = GetHydrated(result, "rues_razon_social");
        var estado = GetHydrated(result, "rues_estado");
        var found = !string.IsNullOrWhiteSpace(razonSocial);

        var dto = new RuesPersonDto(
            Found: found,
            RazonSocial: found ? razonSocial : null,
            Estado: found ? estado : null,
            DocumentNumber: nit,
            MatriculaMercantil: found ? GetHydrated(result, "rues_matricula_mercantil") : null,
            CamaraComercio: found ? GetHydrated(result, "rues_camara_comercio") : null,
            Mode: ResolveMode());

        return (dto, null);
    }

    // Upsert de los campos hidratados en field_values (mismo patrón que RunConsultationCommand).
    private void UpsertHydrated(ProcedureInstance instance, Guid tenantId, IReadOnlyList<HydratedField> hydratedFields)
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
                    FormFieldId = null,
                    FieldKey = field.FieldKey,
                    ValueText = field.ValueText,
                    ValueJson = field.ValueJson,
                    Source = ConsultationSource,
                    CreatedAt = now,
                };
                instance.FieldValues.Add(fieldValue);
                // PK store-generated con Id seteado: marcar Added explícito para forzar INSERT.
                repo.Add(fieldValue);
            }
        }
    }

    private static string? GetHydrated(ConsultationResult result, string fieldKey)
    {
        foreach (var f in result.HydratedFields)
        {
            if (string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                return f.ValueText;
        }

        return null;
    }

    // Modo real|mock informativo para el wizard. Replica la semántica de
    // ConsultationProviderModeOptions.IsMock (VERIFIK_RUES_MODE, default "mock") sin que Application
    // referencie Infrastructure.
    private static string ResolveMode()
    {
        var mode = Environment.GetEnvironmentVariable("VERIFIK_RUES_MODE") ?? "mock";
        return string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) ? "real" : "mock";
    }
}

/// <summary>
/// Persona jurídica resuelta en RUES (sin persistir). <see cref="Found"/> = se hidrató una
/// razón social no vacía. Cuando Found=false, los campos van en null y el frontend cae al ingreso
/// manual (registro sin resultado de consulta).
/// </summary>
public sealed record RuesPersonDto(
    bool Found,
    string? RazonSocial,
    string? Estado,
    string DocumentNumber,
    string? MatriculaMercantil,
    string? CamaraComercio,
    string Mode,
    string DocumentType = "NIT",
    string Source = "RUES");
