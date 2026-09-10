using System.Globalization;
using System.Text.Json;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

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
    ExternalQueryCacheService cacheService,
    ICertificationIngestionService? certificationIngestion = null)
{
    private const string ConsultationSource = "consultation";
    private const string EntityScopeActor = "actor";
    private const string EntityScopeVehicle = "vehicle";
    private const string FieldKeyDocumentType = "document_type";
    private const string FieldKeyDocumentNumber = "document_number";
    private const string FieldKeyPlateOrVin = "plate_or_vin";

    /// <summary>
    /// HU #10974 — fecha en que se consultó el RUNT del vehículo, que el "Certificado de vigencia
    /// SOAT y RTM" declara en su texto introductorio (<c>GenerarFurHandler</c>). No la produce ningún
    /// mapper porque no es un dato de la RESPUESTA del proveedor, sino de la EJECUCIÓN de la consulta.
    /// </summary>
    private const string FieldKeyRuntConsultaFecha = "runt_consulta_fecha";

    /// <summary>Huso horario de Colombia (UTC-5), mismo criterio que el sello de identidad del FUR.</summary>
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

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
                UpsertHydratedFields(instance, tenantId, instanceRepo, WithConsultaFecha(template, cached, now));

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

        UpsertHydratedFields(instance, tenantId, instanceRepo, WithConsultaFecha(template, result, now));

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

        // HU #11304 — el almacén canónico. Va DESPUÉS del guardado en field_values y es best-effort:
        // una consulta ya respondida y persistida no puede caerse porque la ingesta falle. Solo se
        // alimenta del camino real de proveedor: un HIT de caché no trae bundle (la caché guarda
        // HydratedField[], no certificaciones) y no debe reescribir procedencia con una fecha vieja.
        await IngestCertificationsAsync(instanceId, tenantId, result, now, ct);

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
    /// <summary>
    /// HU #10974 — añade <see cref="FieldKeyRuntConsultaFecha"/> a los campos que se van a persistir,
    /// SOLO para consultas de vehículo (el certificado habla del RUNT del vehículo; una consulta de
    /// actor no debe escribir esta llave).
    /// <para>En un HIT de caché se declara la fecha de la consulta <b>ORIGEN</b>
    /// (<see cref="ConsultationResult.QueriedAt"/>), no la del reúso: el documento debe decir cuándo
    /// se consultó el RUNT de verdad. En un MISS, <c>QueriedAt</c> viene null y se usa el instante de
    /// ejecución.</para>
    /// <para>NO muta <paramref name="result"/>: devuelve una lista nueva. El camino de MISS depende de
    /// que la identidad de referencia de <c>result</c> se conserve (ver remarks de la clase).</para>
    /// <para>Deliberadamente NO se cachea: <c>SaveToCacheAsync</c> recibe <c>result.HydratedFields</c>
    /// sin este campo, porque la entrada de caché ya lleva su propia columna <c>QueriedAt</c> y es la
    /// que alimenta el valor en el siguiente reúso.</para>
    /// </summary>
    private static IReadOnlyList<HydratedField> WithConsultaFecha(
        ConsultationTemplate template,
        ConsultationResult result,
        DateTimeOffset ejecutadaAt)
    {
        if (!string.Equals(template.EntityScope, EntityScopeVehicle, StringComparison.OrdinalIgnoreCase))
            return result.HydratedFields;

        var fecha = (result.QueriedAt ?? ejecutadaAt)
            .ToOffset(ColombiaOffset)
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        return [.. result.HydratedFields, new HydratedField(FieldKeyRuntConsultaFecha, fecha, null)];
    }

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

    /// <summary>
    /// Entrega al almacén canónico lo que el mapper certificó (HU #11304, ADR-0041).
    /// </summary>
    /// <remarks>
    /// Best-effort a propósito: cuando esto se llama, la consulta ya se respondió y
    /// <c>field_values</c> ya está guardado. Un fallo del almacén canónico degrada al camino anterior
    /// —el expediente se sigue generando desde <c>field_values</c>— pero no puede convertir una
    /// consulta buena, y cobrada, en un error para el operador.
    /// </remarks>
    private async Task IngestCertificationsAsync(
        Guid instanceId, Guid tenantId, ConsultationResult result, DateTimeOffset now, CancellationToken ct)
    {
        if (certificationIngestion is null || result.Certifications is null || result.FromCache)
            return;

        var provenance = new CertificationProvenance(
            CertificationSourceKind.Consultation,
            result.Provider,
            result.QueriedAt ?? now,
            MapperVersion: ResolveMapperVersion(result.Provider));

        try
        {
            await certificationIngestion.IngestAsync(
                instanceId, tenantId, result.Certifications, provenance, result.RawPayload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silencio deliberado y acotado: no hay logger en esta capa y el dato ya quedó en
            // field_values. La evidencia del fallo, si vuelve a ocurrir, sale del reproceso desde el
            // payload crudo — que es justo lo que esta tabla existe para permitir.
        }
    }

    private static string ResolveMapperVersion(string provider) => provider switch
    {
        "kyverum_runt" => KyverumRuntVehicleResultMapper.MapperVersion,
        "verifik" => VerifikResultMapper.MapperVersion,
        "intempo" => IntempoVehicleResultMapper.MapperVersion,
        _ => CertificationProvenance.UnknownMapperVersion,
    };

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
                // HU #11304 (D2) — una corrección manual sobrevive a la reconsulta. Hasta aquí este
                // bloque sobrescribía sin mirar el `source` previo, así que un operador que arreglaba
                // un dato a mano lo perdía en la siguiente consulta, en silencio y sin rastro. La
                // regla vive en el dominio para que sea la misma que aplica el almacén canónico.
                if (!ConsultationWinsOver(existing, now))
                    continue;

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

    /// <summary>
    /// Llaves de <c>field_values</c> a las que se les aplica la precedencia de D2 (HU #11304).
    /// </summary>
    /// <remarks>
    /// <b>El guardián va acotado a propósito.</b> D2 se decidió sobre las celdas de los certificados;
    /// aplicarlo a todo <c>field_values</c> cambiaría el comportamiento del asistente entero — por
    /// ejemplo, un VIN tecleado con una errata dejaría de corregirse con el que devuelve el RUNT,
    /// porque el valor del operador es <c>user</c> y ganaría siempre. Ese no es el alcance de este
    /// Feature ni lo que el PO decidió.
    /// </remarks>
    private static readonly HashSet<string> CertificationFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "soat_poliza", "soat_aseguradora", "soat_expedicion", "soat_vigencia", "soat_vencimiento",
        SoatGate.FieldKey,
        "rtm_numero", "rtm_entidad", "rtm_expedicion", "rtm_vigencia", "rtm_vencimiento", "rtm_estado",
    };

    /// <summary>
    /// ¿Puede la consulta pisar el valor que ya está guardado? (HU #11304, D2.)
    /// </summary>
    /// <remarks>
    /// Aplica <see cref="CertificationPrecedence"/> —la MISMA regla del almacén canónico— sobre el
    /// <c>source</c> del <c>field_value</c>, y solo sobre las llaves de certificación. Fuera de esas
    /// llaves el comportamiento no cambia: la consulta sigue mandando.
    /// </remarks>
    private static bool ConsultationWinsOver(ProcedureInstanceFieldValue existing, DateTimeOffset now)
    {
        if (!CertificationFieldKeys.Contains(existing.FieldKey))
            return true;

        var incoming = new CertificationProvenance(
            CertificationSourceKind.Consultation, ConsultationSource, now);

        var stored = new CertificationProvenance(
            CertificationSourceCodes.FromCode(existing.Source),
            existing.Source,
            existing.UpdatedAt ?? existing.CreatedAt);

        return CertificationPrecedence.Wins(
            incoming, stored,
            incomingHasValue: true,
            existingHasValue: !string.IsNullOrWhiteSpace(existing.ValueText) || existing.ValueJson is not null);
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
