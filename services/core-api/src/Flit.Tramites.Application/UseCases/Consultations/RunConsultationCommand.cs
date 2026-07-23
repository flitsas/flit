using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Ejecuta una consulta multi-proveedor sobre una instancia de trámite:
/// resuelve el proveedor desde el template, llama al provider y persiste los
/// HydratedFields en field_values con Source="consultation".
/// </summary>
/// <remarks>
/// HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031): ANTES de resolver el proveedor, consulta
/// <see cref="ExternalQueryCacheService"/> por la llave del template (<c>plate_or_vin</c> si
/// <c>EntityScope == "vehicle"</c>; <c>document_type</c>/<c>document_number</c> si
/// <c>EntityScope == "actor"</c>). En HIT reconstruye el <see cref="ConsultationResult"/> desde el
/// payload cacheado SIN llamar <c>provider.ConsultAsync</c> (AC1) y sigue el MISMO camino de
/// <c>UpsertHydratedFields</c> + <c>SaveChangesAsync</c> que el flujo normal (regresión: el
/// comportamiento de escritura de <c>field_values</c> no cambia). En MISS, el flujo original queda
/// intacto byte a byte (incluida la identidad de referencia de <c>result</c>, para no romper
/// <c>RunConsultationHandlerTests.HandleAsync_HappyPath_...BeSameAs</c>) y, tras el
/// <c>SaveChangesAsync</c> exitoso, cachea el resultado (AC2: la próxima consulta dentro del TTL
/// sirve el HIT).
///
/// HU #10885 (Feature #10862, CF-04, botón "Actualizar"): el parámetro opcional
/// <c>forceRefresh</c> (default <c>false</c>, cero regresión) permite al llamador SALTAR
/// deliberadamente el intento de reúso de caché (<see cref="TryReuseFromCacheAsync"/>) y forzar el
/// mismo camino que un MISS: consulta real al proveedor + upsert de la caché con el dato fresco al
/// final (AC2 original). El gate de consentimiento (ADR-0031) vive en
/// <see cref="ExternalQueryCacheService.TryReusePersonAsync"/>, que solo se invoca para intentar un
/// HIT: con <c>forceRefresh=true</c> ese intento ni siquiera ocurre, así que el consentimiento no se
/// evade ni se vuelve a exigir para la reconsulta — simplemente deja de ser relevante porque no hay
/// lectura de caché que gatear.
/// </remarks>
public sealed class RunConsultationHandler(
    IProcedureInstanceRepository instanceRepo,
    ICatalogRepository catalogRepo,
    IConsultationProviderRegistry registry,
    ExternalQueryCacheService cacheService)
{
    private const string ConsultationSource = "consultation";
    private const string EntityScopeActor = "actor";
    private const string EntityScopeVehicle = "vehicle";
    private const string FieldKeyDocumentType = "document_type";
    private const string FieldKeyDocumentNumber = "document_number";
    private const string FieldKeyPlateOrVin = "plate_or_vin";

    public async Task<(ConsultationResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string templateCode,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var template = await catalogRepo.GetConsultationTemplateByCodeAsync(templateCode, ct);
        if (template is null)
            return (null, "template_not_found");

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var sourceCode = template.ExternalDataSource?.Code;

        // HU #10878 — cache-aside ANTES de resolver el proveedor: en HIT ni siquiera hace falta que
        // el provider esté configurado/registrado (AC1: cero llamadas al proveedor externo).
        // HU #10885 (forceRefresh=true, botón "Actualizar"): se salta este bloque por completo — ni
        // siquiera se intenta el HIT — y cae directo al camino de MISS (consulta real + recacheo).
        if (!forceRefresh && !string.IsNullOrWhiteSpace(sourceCode))
        {
            var cached = await TryReuseFromCacheAsync(template, fieldValues, tenantId, sourceCode, now, ct);
            if (cached is not null)
            {
                UpsertHydratedFields(instance, tenantId, instanceRepo, cached.HydratedFields);

                try
                {
                    await instanceRepo.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (IsNotDraftViolation(ex))
                {
                    return (null, "not_draft");
                }

                return (cached, null);
            }
        }

        var providerKey = ResolveProviderKey(template.ExternalRefs);
        if (string.IsNullOrWhiteSpace(providerKey))
            return (null, "provider_not_resolved");

        var provider = registry.Resolve(providerKey);
        if (provider is null)
            return (null, "provider_not_found");

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

        // HU #10878 (AC2): cachea el resultado fresco para reúsos futuros dentro del TTL de la
        // fuente. Best-effort respecto al handler: SavePersonResultAsync/SaveVehicleResultAsync son
        // fail-open (fuente no catalogada o TTL<=0 => no-op), nunca deben tumbar la respuesta ya
        // persistida en field_values.
        if (!string.IsNullOrWhiteSpace(sourceCode))
            await SaveToCacheAsync(template, fieldValues, tenantId, sourceCode, instanceId, result.HydratedFields, now, ct);

        return (result, null);
    }

    /// <summary>
    /// Intenta un HIT de caché según el <c>EntityScope</c> del template. Devuelve <c>null</c> en MISS
    /// (llave ausente, sin consentimiento, no cacheado o vencido) — el llamador sigue con el flujo
    /// normal de proveedor.
    /// </summary>
    private async Task<ConsultationResult?> TryReuseFromCacheAsync(
        ConsultationTemplate template,
        Dictionary<string, string?> fieldValues,
        Guid tenantId,
        string sourceCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.Equals(template.EntityScope, EntityScopeVehicle, StringComparison.OrdinalIgnoreCase))
        {
            if (!fieldValues.TryGetValue(FieldKeyPlateOrVin, out var plateOrVin) || string.IsNullOrWhiteSpace(plateOrVin))
                return null;

            var lookup = await cacheService.TryReuseVehicleAsync(tenantId, sourceCode, plateOrVin, now, ct);
            return lookup.Hit ? BuildCachedResult(sourceCode, lookup) : null;
        }

        if (string.Equals(template.EntityScope, EntityScopeActor, StringComparison.OrdinalIgnoreCase))
        {
            if (!fieldValues.TryGetValue(FieldKeyDocumentType, out var docType) || string.IsNullOrWhiteSpace(docType))
                return null;
            if (!fieldValues.TryGetValue(FieldKeyDocumentNumber, out var docNumber) || string.IsNullOrWhiteSpace(docNumber))
                return null;

            var lookup = await cacheService.TryReusePersonAsync(tenantId, sourceCode, docType, docNumber, now, ct);
            return lookup.Hit ? BuildCachedResult(sourceCode, lookup) : null;
        }

        return null;
    }

    private async Task SaveToCacheAsync(
        ConsultationTemplate template,
        Dictionary<string, string?> fieldValues,
        Guid tenantId,
        string sourceCode,
        Guid instanceId,
        IReadOnlyList<HydratedField> hydratedFields,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.Equals(template.EntityScope, EntityScopeVehicle, StringComparison.OrdinalIgnoreCase))
        {
            if (fieldValues.TryGetValue(FieldKeyPlateOrVin, out var plateOrVin) && !string.IsNullOrWhiteSpace(plateOrVin))
                await cacheService.SaveVehicleResultAsync(tenantId, sourceCode, plateOrVin, instanceId, hydratedFields, now, ct);
            return;
        }

        if (string.Equals(template.EntityScope, EntityScopeActor, StringComparison.OrdinalIgnoreCase))
        {
            if (fieldValues.TryGetValue(FieldKeyDocumentType, out var docType) && !string.IsNullOrWhiteSpace(docType)
                && fieldValues.TryGetValue(FieldKeyDocumentNumber, out var docNumber) && !string.IsNullOrWhiteSpace(docNumber))
            {
                await cacheService.SavePersonResultAsync(tenantId, sourceCode, docType, docNumber, instanceId, hydratedFields, now, ct);
            }
        }
    }

    /// <summary>
    /// Reconstruye un <see cref="ConsultationResult"/> desde un HIT de caché. La caché solo guarda
    /// <c>HydratedField[]</c> (no <c>Checks</c>/<c>Overall</c> por check): se reporta
    /// <c>Overall="green"</c> (el dato reutilizado ya pasó por una consulta previa exitosa) y
    /// <c>Checks=[]</c> — decisión documentada (ADR no especifica el shape exacto de Overall/Checks
    /// en un HIT; el frontend distingue el origen vía <c>FromCache</c>/<c>QueriedAt</c>, no vía Checks).
    /// </summary>
    private static ConsultationResult BuildCachedResult(string sourceCode, CacheLookupResult lookup) =>
        new(
            Provider: sourceCode,
            Overall: "green",
            Checks: [],
            HydratedFields: lookup.Fields ?? [],
            FromCache: true,
            QueriedAt: lookup.QueriedAt);

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
                    // Valor "loose": derivado de consulta, no atado a un form_field.
                    FormFieldId = null,
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
                msg.Contains("borrador", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("draft", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
