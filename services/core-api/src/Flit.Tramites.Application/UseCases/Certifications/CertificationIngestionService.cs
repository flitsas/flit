using Flit.Tramites.Domain.Certifications;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// Implementación del único punto de escritura del almacén canónico (HU #11304, ADR-0041).
/// </summary>
/// <remarks>
/// El trabajo real es la <b>fusión</b>: lo que llega no reemplaza lo guardado, se compara con ello
/// dato a dato. Es lo que hace que reconsultar un trámite sea seguro (un proveedor que esta vez no
/// mandó el número de póliza no borra el que ya se tenía) y que una corrección manual sobreviva a la
/// siguiente consulta (D2) — hoy se pierde en silencio.
/// </remarks>
public sealed class CertificationIngestionService(ICertificationRepository repository)
    : ICertificationIngestionService
{
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public async Task<int> IngestAsync(
        Guid instanceId,
        Guid tenantId,
        CertificationBundle bundle,
        CertificationProvenance provenance,
        RawProviderPayload? rawPayload = null,
        CancellationToken cancellationToken = default)
    {
        if (!bundle.HasAnyValue && rawPayload is null)
            return 0;

        // El payload va primero: las filas certificadas apuntan a la evidencia que las produjo, y sin
        // ese id el reproceso posterior no sabría de qué respuesta salió cada dato.
        var payloadId = await repository.SaveRawPayloadAsync(
            tenantId, instanceId, Sanitized(rawPayload), cancellationToken);

        if (!bundle.HasAnyValue)
            return 0;

        var stamped = provenance with { RawPayloadId = provenance.RawPayloadId ?? payloadId };
        var today = DateOnly.FromDateTime(stamped.ObservedAt.ToOffset(ColombiaOffset).DateTime);
        var existing = await repository.LoadAsync(tenantId, instanceId, cancellationToken);

        var written = 0;
        written += await IngestSoatAsync(instanceId, tenantId, bundle, stamped, existing, today, cancellationToken);
        written += await IngestRtmAsync(instanceId, tenantId, bundle, stamped, existing, today, cancellationToken);
        written += await IngestMerchantAsync(instanceId, tenantId, bundle, stamped, existing, cancellationToken);

        return written;
    }

    private async Task<int> IngestSoatAsync(
        Guid instanceId, Guid tenantId, CertificationBundle bundle, CertificationProvenance provenance,
        CertificationSnapshot existing, DateOnly today, CancellationToken ct)
    {
        var incoming = bundle.SoatHistory.Where(p => p.HasAnyValue).ToList();
        if (incoming.Count == 0)
            return 0;

        var merged = incoming
            .Select(policy =>
            {
                var previous = existing.SoatPolicies
                    .FirstOrDefault(p => p.Certification.NaturalKey() == policy.NaturalKey());

                return previous is null
                    ? new StoredSoatPolicy(policy, provenance)
                    : new StoredSoatPolicy(Merge(policy, provenance, previous), provenance);
            })
            .ToList();

        // La vigente se decide sobre el histórico COMPLETO (lo entrante fusionado + lo que ya había y
        // esta respuesta no menciona): si el proveedor solo devuelve la póliza nueva, la anterior no
        // debe quedar marcada como vigente por inercia.
        var universe = merged
            .Select(m => m.Certification)
            .Concat(existing.SoatPolicies
                .Where(p => merged.All(m => m.Certification.NaturalKey() != p.Certification.NaturalKey()))
                .Select(p => p.Certification))
            .ToList();

        var current = SoatSelection.PickCurrent(universe, today);
        var currentKey = current?.NaturalKey();

        var rows = merged
            .Select(m => m with { IsCurrent = m.Certification.NaturalKey() == currentKey })
            .ToList();

        await repository.UpsertSoatPoliciesAsync(tenantId, instanceId, rows, ct);
        return rows.Count;
    }

    private async Task<int> IngestRtmAsync(
        Guid instanceId, Guid tenantId, CertificationBundle bundle, CertificationProvenance provenance,
        CertificationSnapshot existing, DateOnly today, CancellationToken ct)
    {
        var incoming = bundle.RtmHistory.Where(r => r.HasAnyValue).ToList();
        if (incoming.Count == 0)
            return 0;

        var merged = incoming
            .Select(inspection =>
            {
                var previous = existing.RtmInspections
                    .FirstOrDefault(r => r.Certification.NaturalKey() == inspection.NaturalKey());

                return previous is null
                    ? new StoredRtmInspection(inspection, provenance)
                    : new StoredRtmInspection(Merge(inspection, provenance, previous), provenance);
            })
            .ToList();

        var universe = merged
            .Select(m => m.Certification)
            .Concat(existing.RtmInspections
                .Where(r => merged.All(m => m.Certification.NaturalKey() != r.Certification.NaturalKey()))
                .Select(r => r.Certification))
            .ToList();

        var current = RtmSelection.PickCurrent(universe, today);
        var currentKey = current?.NaturalKey();

        var rows = merged
            .Select(m => m with { IsCurrent = m.Certification.NaturalKey() == currentKey })
            .ToList();

        await repository.UpsertRtmInspectionsAsync(tenantId, instanceId, rows, ct);
        return rows.Count;
    }

    private async Task<int> IngestMerchantAsync(
        Guid instanceId, Guid tenantId, CertificationBundle bundle, CertificationProvenance provenance,
        CertificationSnapshot existing, CancellationToken ct)
    {
        var incoming = bundle.MerchantRegistrations
            .Where(m => m.HasAnyValue && !string.IsNullOrWhiteSpace(m.Nit))
            .ToList();
        if (incoming.Count == 0)
            return 0;

        var rows = incoming
            .Select(registration =>
            {
                var previous = existing.MerchantRegistrations
                    .FirstOrDefault(m => m.Registration.Nit == registration.Nit);

                return previous is null
                    ? new StoredMerchantRegistration(registration, provenance)
                    : new StoredMerchantRegistration(Merge(registration, provenance, previous), provenance);
            })
            .ToList();

        await repository.UpsertMerchantRegistrationsAsync(tenantId, instanceId, rows, ct);
        return rows.Count;
    }

    // ── Fusión dato a dato ────────────────────────────────────────────────────────────────────────

    private static SoatCertification Merge(
        SoatCertification incoming, CertificationProvenance incomingFrom, StoredSoatPolicy stored)
    {
        var previous = stored.Certification;
        var from = stored.Provenance;

        return new SoatCertification(
            CertificationPrecedence.Merge(incoming.PolicyNumber, incomingFrom, previous.PolicyNumber, from),
            CertificationPrecedence.Merge(incoming.Insurer, incomingFrom, previous.Insurer, from),
            CertificationPrecedence.Merge(incoming.IssuedOn, incomingFrom, previous.IssuedOn, from),
            CertificationPrecedence.Merge(incoming.ValidFrom, incomingFrom, previous.ValidFrom, from),
            CertificationPrecedence.Merge(incoming.ValidUntil, incomingFrom, previous.ValidUntil, from),
            CertificationPrecedence.Merge(incoming.Status, incomingFrom, previous.Status, from));
    }

    private static RtmCertification Merge(
        RtmCertification incoming, CertificationProvenance incomingFrom, StoredRtmInspection stored)
    {
        var previous = stored.Certification;
        var from = stored.Provenance;

        return new RtmCertification(
            CertificationPrecedence.Merge(incoming.CertificateNumber, incomingFrom, previous.CertificateNumber, from),
            CertificationPrecedence.Merge(incoming.Cda, incomingFrom, previous.Cda, from),
            CertificationPrecedence.Merge(incoming.IssuedOn, incomingFrom, previous.IssuedOn, from),
            CertificationPrecedence.Merge(incoming.ValidFrom, incomingFrom, previous.ValidFrom, from),
            CertificationPrecedence.Merge(incoming.ValidUntil, incomingFrom, previous.ValidUntil, from),
            CertificationPrecedence.Merge(incoming.Status, incomingFrom, previous.Status, from),
            incoming.InspectionType ?? previous.InspectionType);
    }

    private static MerchantRegistration Merge(
        MerchantRegistration incoming, CertificationProvenance incomingFrom, StoredMerchantRegistration stored)
    {
        var previous = stored.Registration;
        var from = stored.Provenance;

        return new MerchantRegistration(
            incoming.Nit,
            CertificationPrecedence.Merge(incoming.BusinessName, incomingFrom, previous.BusinessName, from),
            CertificationPrecedence.Merge(incoming.RegistrationNumber, incomingFrom, previous.RegistrationNumber, from),
            CertificationPrecedence.Merge(incoming.Status, incomingFrom, previous.Status, from),
            CertificationPrecedence.Merge(incoming.RegisteredOn, incomingFrom, previous.RegisteredOn, from),
            CertificationPrecedence.Merge(incoming.RenewedOn, incomingFrom, previous.RenewedOn, from),
            CertificationPrecedence.Merge(incoming.ChamberOfCommerce, incomingFrom, previous.ChamberOfCommerce, from),
            CertificationPrecedence.Merge(incoming.Category, incomingFrom, previous.Category, from),
            CertificationPrecedence.Merge(incoming.Address, incomingFrom, previous.Address, from),
            CertificationPrecedence.Merge(incoming.City, incomingFrom, previous.City, from),
            // Una lista vacía no borra los representantes que ya se habían pagado y guardado.
            incoming.LegalRepresentatives.Count > 0
                ? incoming.LegalRepresentatives
                : previous.LegalRepresentatives);
    }

    private static RawProviderPayload? Sanitized(RawProviderPayload? payload)
    {
        if (payload is null)
            return null;

        var clean = RawPayloadSanitizer.Sanitize(payload.PayloadJson);
        return clean is null ? null : payload with { PayloadJson = clean };
    }
}
