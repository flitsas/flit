using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13136 (Feature #13115) — reactivación del mandatario: restaura los vínculos con compañías y organismos que
/// retiró la baja (los anota el evento <c>deactivated</c> de la bitácora), sin desplazar al default vigente y
/// respetando el índice único «un activo por compañía, organismo y grupo de origen».
/// </summary>
internal sealed partial class MandateSignerRepository
{
    private async Task<MandateSignerLifecycleResult> PersistReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe, está eliminado (no se reactiva) o ya estaba activo.
        if (signer is null || signer.IsActive || signer.DeletedAt is not null)
        {
            return MandateSignerLifecycleResult.NotApplied;
        }

        await BypassRowSecurityAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var snapshot = await ReadRetirementSnapshotAsync(signer.Id, cancellationToken).ConfigureAwait(false);

        var restoredLinks = new List<MandateSignerLinkRef>();
        var conflictLinks = new List<MandateSignerLinkRef>();
        var restoredDefaults = new List<MandateSignerDefaultRef>();

        if (snapshot is null)
        {
            // Baja anterior a la traza de vínculos (HU #13136): no se sabe cuáles retiró la baja y cuáles una
            // edición, así que se conserva el comportamiento previo (vuelve activo y recupera solo su organismo
            // primario; las compañías se reasignan con «Editar»).
            signer.IsActive = true;
            signer.UpdatedAt = now;
            signer.UpdatedBy = data.ChangedBy;
            await RestaurarOrganismoPrimarioAsync(signer, now, cancellationToken).ConfigureAwait(false);

            AddAudit(
                data.OtTenantId,
                fieldName: "is_active",
                oldValue: JsonSerializer.Serialize(false),
                newValue: JsonSerializer.Serialize(true),
                changedAt: now,
                changedBy: data.ChangedBy,
                correlationId: data.CorrelationId);
            WriteReactivationAudit(data, signer.Id, now, [], [], [], fromSnapshot: false);

            await SaveConTraduccionDeUnicidadAsync(cancellationToken).ConfigureAwait(false);
            return new MandateSignerLifecycleResult(
                true, MandateSignerReassignmentResult.None, [], [], 0, 0, RestoredFromSnapshot: false);
        }

        // Vínculos de esta persona y vínculos ACTIVOS de otros mandatarios en los mismos pares.
        var rows = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var officeIds = snapshot.Links.Select(l => l.TransitOfficeId).Distinct().ToList();
        var companyIds = snapshot.Links.Select(l => l.CompanyTenantId).Distinct().ToList();
        var othersActive = officeIds.Count == 0
            ? []
            : await _context.MandateSignerCompanies
                .Where(c => c.IsActive
                    && c.MandateSignerId != signer.Id
                    && officeIds.Contains(c.TransitOfficeId)
                    && companyIds.Contains(c.CompanyTenantId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var toRestore = new List<MandateSignerCompany>();
        foreach (var link in snapshot.Links)
        {
            var row = rows.FirstOrDefault(r =>
                r.TransitOfficeId == link.TransitOfficeId && r.CompanyTenantId == link.CompanyTenantId);
            if (row is null)
            {
                continue;
            }

            // Un solo activo por compañía, organismo y grupo de origen (índice único parcial, ADR-0066 D1).
            var group = MandateSignerOriginRules.GroupOf(row.ConfiguredByScope);
            var conflicts = othersActive.Any(o =>
                o.TransitOfficeId == link.TransitOfficeId
                && o.CompanyTenantId == link.CompanyTenantId
                && MandateSignerOriginRules.GroupOf(o.ConfiguredByScope) == group);

            if (conflicts)
            {
                conflictLinks.Add(link);
            }
            else
            {
                toRestore.Add(row);
                restoredLinks.Add(link);
            }
        }

        // Todos chocan: reactivar dejaría al mandatario activo sin ningún vínculo. No se cambia nada (409).
        if (snapshot.Links.Count > 0 && toRestore.Count == 0)
        {
            return MandateSignerLifecycleResult.NotApplied with
            {
                ConflictLinks = conflictLinks,
                AllLinksConflict = true,
            };
        }

        signer.IsActive = true;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;

        foreach (var row in toRestore)
        {
            row.IsActive = true;
        }

        // Organismos: los que retiró la baja, más el primario (sin él quedaría inalcanzable, HU #11201).
        await RestaurarOrganismosAsync(signer, snapshot.Offices, now, cancellationToken).ConfigureAwait(false);

        // Defaults: solo recupera el que quedó VACÍO; si otro mandatario ocupa el puesto, no se desplaza.
        var restoredPairs = restoredLinks.ToHashSet();
        foreach (var retired in snapshot.Defaults)
        {
            if (retired.Kind == MandateSignerDefaultRef.CompanyRule && retired.CompanyTenantId is { } companyId)
            {
                if (!restoredPairs.Contains(new MandateSignerLinkRef(retired.TransitOfficeId, companyId)))
                {
                    continue;
                }

                var rule = await _context.CompanyOtMandateRules
                    .FirstOrDefaultAsync(
                        r => r.TransitOfficeId == retired.TransitOfficeId && r.CompanyTenantId == companyId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (rule is not null && rule.DefaultMandateSignerId is null)
                {
                    rule.DefaultMandateSignerId = signer.Id;
                    rule.UpdatedAt = now;
                    rule.UpdatedBy = data.ChangedBy;
                    restoredDefaults.Add(retired);
                }
            }
            else if (retired.Kind == MandateSignerDefaultRef.Office)
            {
                var config = await _context.TransitOfficeMandateConfigs
                    .FirstOrDefaultAsync(c => c.TransitOfficeId == retired.TransitOfficeId, cancellationToken)
                    .ConfigureAwait(false);
                if (config is not null && config.DefaultMandateSignerId is null)
                {
                    config.DefaultMandateSignerId = signer.Id;
                    config.UpdatedAt = now;
                    config.UpdatedBy = data.ChangedBy;
                    restoredDefaults.Add(retired);
                }
            }
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(false),
            newValue: JsonSerializer.Serialize(true),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);
        WriteReactivationAudit(data, signer.Id, now, restoredLinks, conflictLinks, restoredDefaults, fromSnapshot: true);

        await SaveConTraduccionDeUnicidadAsync(cancellationToken).ConfigureAwait(false);

        return new MandateSignerLifecycleResult(
            true, MandateSignerReassignmentResult.None, restoredLinks, conflictLinks, 0, restoredDefaults.Count);
    }

    /// <summary>Vínculos, organismos y defaults que retiró la última baja (inactivación) del mandatario.</summary>
    private sealed record RetirementSnapshot(
        List<Guid> Offices,
        List<MandateSignerLinkRef> Links,
        List<MandateSignerDefaultRef> Defaults);

    private async Task<RetirementSnapshot?> ReadRetirementSnapshotAsync(
        Guid signerId,
        CancellationToken cancellationToken)
    {
        var payload = await _context.TenantConfigAuditLogs
            .AsNoTracking()
            .Where(l => l.TargetEntityId == signerId && l.EntityName == EntityName && l.FieldName == "deactivated")
            .OrderByDescending(l => l.ChangedAt)
            .Select(l => l.NewValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var offices = Items(root, "offices")
                .Where(e => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out _))
                .Select(e => Guid.Parse(e.GetString()!))
                .ToList();
            var links = Items(root, "links")
                .Select(e => new MandateSignerLinkRef(
                    e.GetProperty("officeId").GetGuid(), e.GetProperty("companyTenantId").GetGuid()))
                .ToList();
            var defaults = Items(root, "defaults")
                .Select(e => new MandateSignerDefaultRef(
                    e.GetProperty("kind").GetString() ?? string.Empty,
                    e.GetProperty("officeId").GetGuid(),
                    e.TryGetProperty("companyTenantId", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetGuid()
                        : null))
                .ToList();

            return new RetirementSnapshot(offices, links, defaults);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Traza ilegible: se trata como baja anterior a la traza (comportamiento previo, sin romper).
            return null;
        }
    }

    private static List<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? [.. arr.EnumerateArray()]
            : [];

    /// <summary>Reactiva los organismos que retiró la baja (o los crea) y garantiza el primario.</summary>
    private async Task RestaurarOrganismosAsync(
        MandateSigner signer,
        IReadOnlyList<Guid> retiredOffices,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var wanted = retiredOffices.Append(signer.TransitOfficeId).Distinct().ToHashSet();
        var rows = await _context.MandateSignerTransitOffices
            .Where(o => o.MandateSignerId == signer.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows.Where(r => wanted.Contains(r.TransitOfficeId)))
        {
            row.IsActive = true;
        }

        var existing = rows.Select(r => r.TransitOfficeId).ToHashSet();
        foreach (var officeId in wanted.Where(id => !existing.Contains(id)))
        {
            _context.MandateSignerTransitOffices.Add(NewOffice(signer.Id, officeId, now));
        }
    }
}
