using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Resultado de un intento de reúso de la caché cross-trámite (HU #10878, ADR-0030/ADR-0031).
/// <see cref="MissReason"/> ∈ {"invalid_key", "no_consent", "not_found_or_expired", null (si Hit)}.
/// </summary>
public sealed record CacheLookupResult(
    bool Hit,
    IReadOnlyList<HydratedField>? Fields,
    DateTimeOffset? QueriedAt,
    string? MissReason);

/// <summary>
/// Cache-aside de consultas externas cross-trámite (HU #10878, Feature #10862, CF-04). Consumido por
/// <see cref="RunConsultationHandler"/>, <see cref="RuntPersonLookupHandler"/> y
/// <see cref="RuesPersonLookupHandler"/> ANTES de resolver/llamar al proveedor externo. NO modifica
/// el contrato <c>IConsultationProvider</c>/<c>IConsultationProviderRegistry</c> (ADR-0020): es una
/// capa previa a la resolución de proveedor, no un provider más.
///
/// <para><b>Gate de consentimiento (ADR-0031, FAIL-SAFE):</b> <see cref="TryReusePersonAsync"/> SOLO
/// sirve un HIT si existe un <see cref="PersonDataConsent"/> con
/// <see cref="PersonDataConsentStatus.Granted"/> vigente para <c>(tenant, tipoDoc, documento)</c>.
/// Sin fila, o con cualquier otro estado, se trata como <c>MissReason="no_consent"</c> — NUNCA
/// bloquea el trámite, solo desactiva la optimización de reúso para esa persona (cae a consulta
/// fresca). El reúso de VEHÍCULO (<see cref="TryReuseVehicleAsync"/>) no lleva gate: no es un dato
/// personal.</para>
/// </summary>
public sealed class ExternalQueryCacheService(
    IExternalQueryCacheRepository cacheRepo,
    IPersonDataConsentRepository consentRepo,
    ICatalogRepository catalogRepo)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CacheLookupResult> TryReusePersonAsync(
        Guid tenantId,
        string sourceCode,
        string documentType,
        string documentNumber,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode) ||
            string.IsNullOrWhiteSpace(documentType) ||
            string.IsNullOrWhiteSpace(documentNumber))
        {
            return new CacheLookupResult(false, null, null, "invalid_key");
        }

        var docType = documentType.Trim().ToUpperInvariant();
        var docNumber = documentNumber.Trim();

        // Gate de consentimiento PRIMERO (ADR-0031): sin `granted`, ni siquiera se consulta la caché.
        var consent = await consentRepo.GetAsync(tenantId, docType, docNumber, ct);
        if (consent is null || consent.Status != PersonDataConsentStatus.Granted)
            return new CacheLookupResult(false, null, null, "no_consent");

        var source = await catalogRepo.GetExternalDataSourceByCodeAsync(sourceCode, ct);
        if (source is null)
            return new CacheLookupResult(false, null, null, "not_found_or_expired");

        var entry = await cacheRepo.FindPersonAsync(tenantId, source.Id, docType, docNumber, ct);
        if (entry is null || now >= entry.ExpiresAt)
            return new CacheLookupResult(false, null, null, "not_found_or_expired");

        await TouchBestEffortAsync(entry, now, ct);

        return new CacheLookupResult(true, DeserializePayload(entry.Payload), entry.QueriedAt, null);
    }

    /// <summary>Reúso de vehículo — SIN gate de consentimiento (no es un dato personal).</summary>
    public async Task<CacheLookupResult> TryReuseVehicleAsync(
        Guid tenantId,
        string sourceCode,
        string vehicleIdentifier,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode) || string.IsNullOrWhiteSpace(vehicleIdentifier))
            return new CacheLookupResult(false, null, null, "invalid_key");

        var identifier = NormalizeVehicleIdentifier(vehicleIdentifier);

        var source = await catalogRepo.GetExternalDataSourceByCodeAsync(sourceCode, ct);
        if (source is null)
            return new CacheLookupResult(false, null, null, "not_found_or_expired");

        var entry = await cacheRepo.FindVehicleAsync(tenantId, source.Id, identifier, ct);
        if (entry is null || now >= entry.ExpiresAt)
            return new CacheLookupResult(false, null, null, "not_found_or_expired");

        await TouchBestEffortAsync(entry, now, ct);

        return new CacheLookupResult(true, DeserializePayload(entry.Payload), entry.QueriedAt, null);
    }

    /// <summary>
    /// Cachea el resultado de una consulta de PERSONA recién hecha. <c>expires_at = now +
    /// (fuente.CacheTtlHours ?? DefaultTtlHours)</c>. Fuente no catalogada → no cachea (fail-open:
    /// nunca tumba al handler llamador). Upsert por la llave única (tenant, fuente, tipoDoc, documento).
    /// </summary>
    public async Task SavePersonResultAsync(
        Guid tenantId,
        string sourceCode,
        string documentType,
        string documentNumber,
        Guid? sourceProcedureInstanceId,
        IReadOnlyList<HydratedField> fields,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode) ||
            string.IsNullOrWhiteSpace(documentType) ||
            string.IsNullOrWhiteSpace(documentNumber))
        {
            return;
        }

        var source = await catalogRepo.GetExternalDataSourceByCodeAsync(sourceCode, ct);
        if (source is null)
            return;

        var ttlHours = source.CacheTtlHours ?? ExternalQueryCacheRules.DefaultTtlHours;
        if (ttlHours <= 0)
            return; // TTL 0/negativo configurado explícitamente en el catálogo: no cachear.

        var docType = documentType.Trim().ToUpperInvariant();
        var docNumber = documentNumber.Trim();
        var payload = SerializePayload(fields);
        var expiresAt = now.AddHours(ttlHours);

        var existing = await cacheRepo.FindPersonAsync(tenantId, source.Id, docType, docNumber, ct);
        if (existing is not null)
        {
            existing.Payload = payload;
            existing.QueriedAt = now;
            existing.ExpiresAt = expiresAt;
            existing.SourceProcedureInstanceId = sourceProcedureInstanceId;
            existing.UpdatedAt = now;
        }
        else
        {
            await cacheRepo.AddAsync(new ExternalQueryCacheEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalDataSourceId = source.Id,
                SubjectKind = ExternalQueryCacheRules.SubjectKindPerson,
                DocumentType = docType,
                DocumentNumber = docNumber,
                Payload = payload,
                QueriedAt = now,
                ExpiresAt = expiresAt,
                SourceProcedureInstanceId = sourceProcedureInstanceId,
                CreatedAt = now,
            }, ct);
        }

        await cacheRepo.SaveChangesAsync(ct);
    }

    /// <summary>Análogo de <see cref="SavePersonResultAsync"/> para vehículo (identificador normalizado).</summary>
    public async Task SaveVehicleResultAsync(
        Guid tenantId,
        string sourceCode,
        string vehicleIdentifier,
        Guid? sourceProcedureInstanceId,
        IReadOnlyList<HydratedField> fields,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode) || string.IsNullOrWhiteSpace(vehicleIdentifier))
            return;

        var source = await catalogRepo.GetExternalDataSourceByCodeAsync(sourceCode, ct);
        if (source is null)
            return;

        var ttlHours = source.CacheTtlHours ?? ExternalQueryCacheRules.DefaultTtlHours;
        if (ttlHours <= 0)
            return;

        var identifier = NormalizeVehicleIdentifier(vehicleIdentifier);
        var payload = SerializePayload(fields);
        var expiresAt = now.AddHours(ttlHours);

        var existing = await cacheRepo.FindVehicleAsync(tenantId, source.Id, identifier, ct);
        if (existing is not null)
        {
            existing.Payload = payload;
            existing.QueriedAt = now;
            existing.ExpiresAt = expiresAt;
            existing.SourceProcedureInstanceId = sourceProcedureInstanceId;
            existing.UpdatedAt = now;
        }
        else
        {
            await cacheRepo.AddAsync(new ExternalQueryCacheEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ExternalDataSourceId = source.Id,
                SubjectKind = ExternalQueryCacheRules.SubjectKindVehicle,
                VehicleIdentifier = identifier,
                Payload = payload,
                QueriedAt = now,
                ExpiresAt = expiresAt,
                SourceProcedureInstanceId = sourceProcedureInstanceId,
                CreatedAt = now,
            }, ct);
        }

        await cacheRepo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Best-effort: incrementa el contador de reúso de un HIT. Un fallo al persistirlo NUNCA debe
    /// tumbar el hit ya resuelto (es telemetría, no parte del contrato funcional).
    /// </summary>
    private async Task TouchBestEffortAsync(ExternalQueryCacheEntry entry, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            entry.ReuseCount += 1;
            entry.LastReusedAt = now;
            await cacheRepo.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // best-effort: no propagar — el HIT ya se resolvió con el payload correcto.
        }
    }

    private static string NormalizeVehicleIdentifier(string raw) => raw.Trim().ToUpperInvariant();

    private static string SerializePayload(IReadOnlyList<HydratedField> fields) =>
        JsonSerializer.Serialize(fields, JsonOptions);

    private static List<HydratedField> DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<HydratedField>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
