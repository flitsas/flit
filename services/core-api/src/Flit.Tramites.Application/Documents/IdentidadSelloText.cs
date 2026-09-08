using System.Globalization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Sello multilínea de validación biométrica (HU #10488 / #11015 / #11018).
/// Misma leyenda que el FUR usa junto a la rúbrica de identidad.
/// </summary>
public static class IdentidadSelloText
{
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static string Build(ProcedureInstanceBiometricValidation v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var doc = $"{v.DocumentType} {v.DocumentNumber}".Trim();
        var uuid = string.IsNullOrWhiteSpace(v.KyverumVerificationId) ? v.Id.ToString("D") : v.KyverumVerificationId!;
        var firma = string.IsNullOrWhiteSpace(v.CertificateHash) ? "no disponible" : v.CertificateHash!;
        var aprob = v.ValidatedAt is { } va
            ? va.ToOffset(ColombiaOffset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        var vence = v.ValidUntil is { } vu
            ? vu.ToOffset(ColombiaOffset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        return $"Validación biométrica {doc}\nUUID {uuid}\nFirma {firma}\nAprob {aprob} · Vence {vence}";
    }
}
