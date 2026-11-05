using System.Globalization;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Sello multilínea de validación biométrica (HU #10488 / #11015 / #11018).
/// Misma leyenda que el FUR usa junto a la rúbrica de identidad.
/// </summary>
public static class IdentidadSelloText
{

    public static string Build(ProcedureInstanceBiometricValidation v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var vence = v.ValidUntil is { } vu
            ? vu.ToOffset(ColombiaTime.Offset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        return $"{Encabezado(v)} · Vence {vence}";
    }

    /// <summary>
    /// Sello del MANDATARIO: la misma leyenda que el de las partes (documento, UUID, firma y aprobación). Su
    /// validación no vence a los 30 días (HU #13130b): vale mientras el mandatario esté vigente, así que «Vence» es
    /// el fin de su vigencia por rango y no aparece con vigencia fija.
    /// </summary>
    public static string BuildMandatario(ProcedureInstanceBiometricValidation v, DateOnly? vigenteHasta)
    {
        ArgumentNullException.ThrowIfNull(v);
        return vigenteHasta is { } hasta
            ? $"{Encabezado(v)} · Vence {hasta.ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture)}"
            : Encabezado(v);
    }

    /// <summary>
    /// Firma del sello. Kyverum: la serie <c>firmaSerie</c> del certificado (<c>CertificateHash</c>), sin cambios. HU #14302 —
    /// validación MANUAL aprobada: solo la serie de la carta de identidad que FLIT emitió (<c>CertificateHash</c> es de Kyverum y
    /// no se toca). Sin ninguno de los dos, «no disponible».
    /// </summary>
    private static string Firma(ProcedureInstanceBiometricValidation v)
    {
        if (!string.IsNullOrWhiteSpace(v.CertificateHash))
            return v.CertificateHash!;

        // Solo la serie: el hash completo vive en la carta y en CartaSha256 (el renderer trunca las líneas largas).
        return v.TieneCartaIdentidad && !string.IsNullOrWhiteSpace(v.CartaSerie)
            ? v.CartaSerie!
            : "no disponible";
    }

    private static string Encabezado(ProcedureInstanceBiometricValidation v)
    {
        var doc = $"{v.DocumentType} {v.DocumentNumber}".Trim();
        var uuid = string.IsNullOrWhiteSpace(v.KyverumVerificationId) ? v.Id.ToString("D") : v.KyverumVerificationId!;
        var firma = Firma(v);
        var aprob = v.ValidatedAt is { } va
            ? va.ToOffset(ColombiaTime.Offset).ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture) : "-";
        return $"Validación biométrica {doc}\nUUID {uuid}\nFirma {firma}\nAprob {aprob}";
    }
}
