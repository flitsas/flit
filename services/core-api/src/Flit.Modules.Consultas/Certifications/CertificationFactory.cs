using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Certifications.Normalization;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// Construye certificaciones canónicas a partir del texto crudo de un proveedor, aplicando los
/// normalizadores compartidos (HU #11303, Feature #11301, ADR-0041).
/// </summary>
/// <remarks>
/// Existe para que los tres mappers de vehículo no repitan —ni diverjan en— la misma normalización.
/// Cada mapper sabe cómo se llaman los campos en <i>su</i> proveedor; ninguno decide cómo se lee una
/// fecha o qué significa un estado. Añadir un cuarto proveedor es implementar su mapper y llamar aquí:
/// cero cambios en los tres anteriores.
/// </remarks>
public static class CertificationFactory
{
    public static SoatCertification Soat(
        string? policyNumber,
        string? insurer,
        string? issuedOn,
        string? validFrom,
        string? validUntil,
        string? status) =>
        new(CertificateNumberNormalizer.Normalize(policyNumber),
            EntityNameNormalizer.Normalize(insurer),
            ColombianCertificateDate.Parse(issuedOn),
            ColombianCertificateDate.Parse(validFrom),
            ColombianCertificateDate.Parse(validUntil),
            VigencyStatusNormalizer.ForVehicle(status));

    public static RtmCertification Rtm(
        string? certificateNumber,
        string? cda,
        string? issuedOn,
        string? validFrom,
        string? validUntil,
        string? status,
        string? inspectionType = null) =>
        new(CertificateNumberNormalizer.Normalize(certificateNumber),
            EntityNameNormalizer.Normalize(cda),
            ColombianCertificateDate.Parse(issuedOn),
            ColombianCertificateDate.Parse(validFrom),
            ColombianCertificateDate.Parse(validUntil),
            VigencyStatusNormalizer.ForVehicle(status),
            EntityNameNormalizer.Normalize(inspectionType).Value);

    public static VehicleRegistrationFacts Vehicle(string? fechaMatricula) =>
        new(ColombianCertificateDate.Parse(fechaMatricula));

    /// <summary>
    /// Arma el bundle de vehículo descartando las filas que no aportan ningún dato. Una entrada vacía
    /// en el histórico del proveedor no debe producir una fila en la base ni competir por ser la
    /// vigente.
    /// </summary>
    public static CertificationBundle? VehicleBundle(
        IEnumerable<SoatCertification> soat,
        IEnumerable<RtmCertification> rtm,
        VehicleRegistrationFacts vehicle,
        DateOnly today)
    {
        var policies = soat.Where(p => p.HasAnyValue).ToList();
        var inspections = rtm.Where(r => r.HasAnyValue).ToList();

        if (policies.Count == 0 && inspections.Count == 0 && !vehicle.HasAnyValue)
            return null;

        return CertificationBundle.ForVehicle(
            Order(policies, p => p.ValidUntil.Value, today),
            Order(inspections, r => r.ValidUntil.Value, today),
            vehicle);
    }

    /// <summary>
    /// Deja primero la que cubre hoy y luego las demás por vencimiento descendente. No decide cuál es
    /// la vigente —eso es de <see cref="SoatSelection"/>/<see cref="RtmSelection"/>—, solo entrega el
    /// histórico en un orden estable para que la persistencia sea determinista.
    /// </summary>
    private static List<T> Order<T>(
        List<T> items, Func<T, DateOnly?> validUntil, DateOnly today) =>
        items
            .OrderByDescending(i => validUntil(i) >= today)
            .ThenByDescending(i => validUntil(i) ?? DateOnly.MinValue)
            .ToList();
}
