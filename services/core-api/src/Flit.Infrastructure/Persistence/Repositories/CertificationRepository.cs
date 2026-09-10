using System.Text.Json;
using Flit.Tramites.Application.UseCases.Certifications;
using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia del modelo canónico de certificaciones (HU #11302, ADR-0041).
/// </summary>
/// <remarks>
/// Aislamiento por tenant en el <c>WHERE</c>, como el resto de repositorios: la RLS de las tablas es
/// defensa en profundidad y hoy no se evalúa (la aplicación conecta como owner y ninguna tabla tiene
/// <c>FORCE ROW LEVEL SECURITY</c>).
///
/// <para>Las escrituras son <b>upsert por llave natural</b>. Reconsultar el mismo trámite actualiza la
/// fila existente en vez de duplicarla, y dos pólizas distintas del histórico conviven porque su llave
/// difiere. Las filas con <c>frozen_at</c> no se tocan: el trámite ya está radicado y su expediente
/// debe quedar estable.</para>
/// </remarks>
internal sealed class CertificationRepository(FlitDbContext db) : ICertificationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CertificationSnapshot> LoadAsync(
        Guid tenantId, Guid instanceId, CancellationToken cancellationToken)
    {
        var soat = await db.VehicleSoatPolicies.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.ValidUntil)
            .ToListAsync(cancellationToken);

        var rtm = await db.VehicleRtmInspections.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.ValidUntil)
            .ToListAsync(cancellationToken);

        var companies = await db.CompanyRegistrations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .OrderBy(x => x.Nit)
            .ToListAsync(cancellationToken);

        return new CertificationSnapshot(
            soat.Select(ToDomain).ToList(),
            rtm.Select(ToDomain).ToList(),
            companies.Select(ToDomain).ToList());
    }

    public async Task<Guid?> SaveRawPayloadAsync(
        Guid tenantId, Guid instanceId, RawProviderPayload? payload, CancellationToken cancellationToken)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.PayloadJson))
            return null;

        var entity = new ExternalQueryPayload
        {
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ProviderKey = Truncate(payload.ProviderKey, 40) ?? string.Empty,
            SubjectKind = payload.SubjectKind,
            SubjectKey = Truncate(payload.SubjectKey, 40),
            Payload = payload.PayloadJson,
            QueriedAt = payload.QueriedAt,
            // D6: retención indefinida — ExpiresAt se deja en null a propósito.
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await db.ExternalQueryPayloads.AddAsync(entity, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task UpsertSoatPoliciesAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredSoatPolicy> policies,
        CancellationToken cancellationToken)
    {
        if (policies.Count == 0)
            return;

        var existing = await db.VehicleSoatPolicies
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .ToListAsync(cancellationToken);

        // La vigente es única por trámite (índice parcial). Se baja la bandera de todas antes de
        // levantarla en la nueva, o el UPDATE choca contra el índice a mitad de camino.
        if (policies.Any(p => p.IsCurrent))
        {
            foreach (var row in existing.Where(r => r.IsCurrent && r.FrozenAt is null))
                row.IsCurrent = false;
        }

        foreach (var policy in policies)
        {
            var naturalKey = policy.Certification.NaturalKey();
            var row = existing.FirstOrDefault(r => r.NaturalKey == naturalKey);

            if (row is null)
            {
                row = new VehicleSoatPolicy
                {
                    TenantId = tenantId,
                    ProcedureInstanceId = instanceId,
                    NaturalKey = naturalKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                existing.Add(row);
                await db.VehicleSoatPolicies.AddAsync(row, cancellationToken);
            }
            else if (row.FrozenAt is not null)
            {
                // Expediente radicado: no se reescribe.
                continue;
            }

            Apply(row, policy);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertRtmInspectionsAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredRtmInspection> inspections,
        CancellationToken cancellationToken)
    {
        if (inspections.Count == 0)
            return;

        var existing = await db.VehicleRtmInspections
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .ToListAsync(cancellationToken);

        if (inspections.Any(i => i.IsCurrent))
        {
            foreach (var row in existing.Where(r => r.IsCurrent && r.FrozenAt is null))
                row.IsCurrent = false;
        }

        foreach (var inspection in inspections)
        {
            var naturalKey = inspection.Certification.NaturalKey();
            var row = existing.FirstOrDefault(r => r.NaturalKey == naturalKey);

            if (row is null)
            {
                row = new VehicleRtmInspection
                {
                    TenantId = tenantId,
                    ProcedureInstanceId = instanceId,
                    NaturalKey = naturalKey,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                existing.Add(row);
                await db.VehicleRtmInspections.AddAsync(row, cancellationToken);
            }
            else if (row.FrozenAt is not null)
            {
                continue;
            }

            Apply(row, inspection);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertMerchantRegistrationsAsync(
        Guid tenantId, Guid instanceId, IReadOnlyList<StoredMerchantRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (registrations.Count == 0)
            return;

        var existing = await db.CompanyRegistrations
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId)
            .ToListAsync(cancellationToken);

        foreach (var registration in registrations)
        {
            var nit = registration.Registration.Nit?.Trim();
            if (string.IsNullOrEmpty(nit))
                continue;

            var row = existing.FirstOrDefault(r => r.Nit == nit);

            if (row is null)
            {
                row = new CompanyRegistration
                {
                    TenantId = tenantId,
                    ProcedureInstanceId = instanceId,
                    Nit = Truncate(nit, 20)!,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                existing.Add(row);
                await db.CompanyRegistrations.AddAsync(row, cancellationToken);
            }
            else if (row.FrozenAt is not null)
            {
                continue;
            }

            Apply(row, registration);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> FreezeAsync(
        Guid tenantId, Guid instanceId, DateTimeOffset frozenAt, CancellationToken cancellationToken)
    {
        var frozen = 0;

        frozen += await db.VehicleSoatPolicies
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId && x.FrozenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FrozenAt, frozenAt), cancellationToken);

        frozen += await db.VehicleRtmInspections
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId && x.FrozenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FrozenAt, frozenAt), cancellationToken);

        frozen += await db.CompanyRegistrations
            .Where(x => x.TenantId == tenantId && x.ProcedureInstanceId == instanceId && x.FrozenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FrozenAt, frozenAt), cancellationToken);

        return frozen;
    }

    // ── Dominio → fila ────────────────────────────────────────────────────────────────────────────

    private static void Apply(VehicleSoatPolicy row, StoredSoatPolicy source)
    {
        var c = source.Certification;

        row.PolicyNumber = Truncate(c.PolicyNumber.Value, 60);
        row.PolicyNumberRaw = c.PolicyNumber.Raw;
        row.InsurerName = Truncate(c.Insurer.Value, 400);
        row.InsurerNameRaw = c.Insurer.Raw;
        row.IssuedOn = c.IssuedOn.Value;
        row.IssuedOnRaw = c.IssuedOn.Raw;
        row.ValidFrom = c.ValidFrom.Value;
        row.ValidFromRaw = c.ValidFrom.Raw;
        row.ValidUntil = c.ValidUntil.Value;
        row.ValidUntilRaw = c.ValidUntil.Raw;
        row.VigencyStatus = VigencyStatusCodes.ToCode(c.Status.Value);
        row.VigencyStatusRaw = c.Status.Raw;
        row.IsCurrent = source.IsCurrent;

        ApplyProvenance(source.Provenance, c.NormalizationIssues(),
            k => row.SourceKind = k, p => row.ProviderKey = p, o => row.ObservedAt = o,
            r => row.RawPayloadId = r, m => row.MapperVersion = m, i => row.NormalizationIssues = i);
    }

    private static void Apply(VehicleRtmInspection row, StoredRtmInspection source)
    {
        var c = source.Certification;

        row.CertificateNumber = Truncate(c.CertificateNumber.Value, 60);
        row.CertificateNumberRaw = c.CertificateNumber.Raw;
        row.CdaName = Truncate(c.Cda.Value, 400);
        row.CdaNameRaw = c.Cda.Raw;
        row.IssuedOn = c.IssuedOn.Value;
        row.IssuedOnRaw = c.IssuedOn.Raw;
        row.ValidFrom = c.ValidFrom.Value;
        row.ValidFromRaw = c.ValidFrom.Raw;
        row.ValidUntil = c.ValidUntil.Value;
        row.ValidUntilRaw = c.ValidUntil.Raw;
        row.VigencyStatus = VigencyStatusCodes.ToCode(c.Status.Value);
        row.VigencyStatusRaw = c.Status.Raw;
        row.InspectionType = Truncate(c.InspectionType, 60);
        row.IsCurrent = source.IsCurrent;

        ApplyProvenance(source.Provenance, c.NormalizationIssues(),
            k => row.SourceKind = k, p => row.ProviderKey = p, o => row.ObservedAt = o,
            r => row.RawPayloadId = r, m => row.MapperVersion = m, i => row.NormalizationIssues = i);
    }

    private static void Apply(CompanyRegistration row, StoredMerchantRegistration source)
    {
        var m = source.Registration;

        row.BusinessName = Truncate(m.BusinessName.Value, 400);
        row.BusinessNameRaw = m.BusinessName.Raw;
        row.RegistrationNumber = Truncate(m.RegistrationNumber.Value, 60);
        row.RegistrationNumberRaw = m.RegistrationNumber.Raw;
        row.RegistrationStatus = VigencyStatusCodes.ToCode(m.Status.Value);
        row.RegistrationStatusRaw = m.Status.Raw;
        row.RegisteredOn = m.RegisteredOn.Value;
        row.RegisteredOnRaw = m.RegisteredOn.Raw;
        row.RenewedOn = m.RenewedOn.Value;
        row.RenewedOnRaw = m.RenewedOn.Raw;
        row.ChamberOfCommerce = Truncate(m.ChamberOfCommerce.Value, 400);
        row.ChamberOfCommerceRaw = m.ChamberOfCommerce.Raw;
        row.Category = Truncate(m.Category.Value, 400);
        row.CategoryRaw = m.Category.Raw;
        row.Address = Truncate(m.Address.Value, 400);
        row.AddressRaw = m.Address.Raw;
        row.City = Truncate(m.City.Value, 400);
        row.CityRaw = m.City.Raw;
        row.LegalRepresentatives = JsonSerializer.Serialize(m.LegalRepresentatives, JsonOptions);

        ApplyProvenance(source.Provenance, m.NormalizationIssues(),
            k => row.SourceKind = k, p => row.ProviderKey = p, o => row.ObservedAt = o,
            r => row.RawPayloadId = r, mv => row.MapperVersion = mv, i => row.NormalizationIssues = i);
    }

    private static void ApplyProvenance(
        CertificationProvenance provenance, IReadOnlyList<string> issues,
        Action<string> setSourceKind, Action<string> setProviderKey, Action<DateTimeOffset> setObservedAt,
        Action<Guid?> setRawPayloadId, Action<string> setMapperVersion, Action<string> setIssues)
    {
        setSourceKind(CertificationSourceCodes.ToCode(provenance.Source));
        setProviderKey(Truncate(provenance.ProviderKey, 40) ?? CertificationProvenance.UnknownMapperVersion);
        setObservedAt(provenance.ObservedAt);
        setRawPayloadId(provenance.RawPayloadId);
        setMapperVersion(Truncate(provenance.MapperVersion, 20) ?? CertificationProvenance.UnknownMapperVersion);
        setIssues(JsonSerializer.Serialize(issues, JsonOptions));
    }

    // ── Fila → dominio ────────────────────────────────────────────────────────────────────────────

    private static StoredSoatPolicy ToDomain(VehicleSoatPolicy row) => new(
        new SoatCertification(
            new CertifiedNumber(row.PolicyNumber, row.PolicyNumberRaw),
            new CertifiedName(row.InsurerName, row.InsurerNameRaw),
            new CertifiedDate(row.IssuedOn, row.IssuedOnRaw),
            new CertifiedDate(row.ValidFrom, row.ValidFromRaw),
            new CertifiedDate(row.ValidUntil, row.ValidUntilRaw),
            new CertifiedStatus(VigencyStatusCodes.FromCode(row.VigencyStatus), row.VigencyStatusRaw)),
        ToProvenance(row.SourceKind, row.ProviderKey, row.ObservedAt, row.RawPayloadId, row.MapperVersion),
        row.IsCurrent,
        row.FrozenAt);

    private static StoredRtmInspection ToDomain(VehicleRtmInspection row) => new(
        new RtmCertification(
            new CertifiedNumber(row.CertificateNumber, row.CertificateNumberRaw),
            new CertifiedName(row.CdaName, row.CdaNameRaw),
            new CertifiedDate(row.IssuedOn, row.IssuedOnRaw),
            new CertifiedDate(row.ValidFrom, row.ValidFromRaw),
            new CertifiedDate(row.ValidUntil, row.ValidUntilRaw),
            new CertifiedStatus(VigencyStatusCodes.FromCode(row.VigencyStatus), row.VigencyStatusRaw),
            row.InspectionType),
        ToProvenance(row.SourceKind, row.ProviderKey, row.ObservedAt, row.RawPayloadId, row.MapperVersion),
        row.IsCurrent,
        row.FrozenAt);

    private static StoredMerchantRegistration ToDomain(CompanyRegistration row) => new(
        new MerchantRegistration(
            row.Nit,
            new CertifiedName(row.BusinessName, row.BusinessNameRaw),
            new CertifiedNumber(row.RegistrationNumber, row.RegistrationNumberRaw),
            new CertifiedStatus(VigencyStatusCodes.FromCode(row.RegistrationStatus), row.RegistrationStatusRaw),
            new CertifiedDate(row.RegisteredOn, row.RegisteredOnRaw),
            new CertifiedDate(row.RenewedOn, row.RenewedOnRaw),
            new CertifiedName(row.ChamberOfCommerce, row.ChamberOfCommerceRaw),
            new CertifiedName(row.Category, row.CategoryRaw),
            new CertifiedName(row.Address, row.AddressRaw),
            new CertifiedName(row.City, row.CityRaw),
            DeserializeRepresentatives(row.LegalRepresentatives)),
        ToProvenance(row.SourceKind, row.ProviderKey, row.ObservedAt, row.RawPayloadId, row.MapperVersion),
        row.FrozenAt);

    private static CertificationProvenance ToProvenance(
        string sourceKind, string providerKey, DateTimeOffset observedAt, Guid? rawPayloadId, string mapperVersion) =>
        new(CertificationSourceCodes.FromCode(sourceKind), providerKey, observedAt, rawPayloadId, mapperVersion);

    /// <summary>
    /// Un JSON corrupto en una fila no puede tumbar la generación del expediente completo: se degrada
    /// a lista vacía. Que el resto del registro mercantil siga imprimiéndose es preferible a un 500.
    /// </summary>
    private static List<LegalRepresentative> DeserializeRepresentatives(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<LegalRepresentative>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
