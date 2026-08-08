using Flit.Tramites.Domain.Certifications;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// <b>Único punto de lectura documental</b> de certificaciones (HU #11305, ADR-0041).
/// </summary>
/// <remarks>
/// Generar el expediente pasa a costar <b>cero llamadas externas</b>. Hoy el generador del PDF
/// consulta el RUES <b>en vivo</b> cada vez que se regenera un documento —cobrado, y dejando el
/// expediente a merced de que el proveedor esté arriba—; no por capricho, sino porque el snapshot
/// vivía en <c>field_values</c>, que es inmutable fuera de borrador, y una compañía sin snapshot no
/// tenía otra forma de conseguirlo.
///
/// <para>El lector resuelve en tres saltos y <b>nunca</b> consulta: tabla canónica → respaldo sobre lo
/// que ya está en <c>field_values</c> (para los trámites anteriores al despliegue) → nada. «Nada»
/// significa celda en blanco o certificado no emitido, que es la decisión explícita del PO
/// (D1/D4).</para>
/// </remarks>
public interface ICertificationReader
{
    /// <summary>
    /// Lo certificado para el trámite, listo para los generadores.
    /// </summary>
    /// <param name="fieldValues">
    /// <c>field_key → value_text</c> de la instancia, que el llamador ya tiene cargado. Se recibe en
    /// vez de volver a leerlo para no acoplar el lector al repositorio de trámites y para que el
    /// respaldo sobre datos anteriores al despliegue no cueste una consulta extra.
    /// </param>
    Task<CertificationView> ForDocumentsAsync(
        Guid instanceId,
        Guid tenantId,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken cancellationToken);
}

/// <summary>Lo que el expediente necesita saber, ya resuelto y tipado.</summary>
public sealed record CertificationView(
    SoatCertification? Soat,
    CertificationProvenance? SoatFrom,
    RtmCertification? Rtm,
    CertificationProvenance? RtmFrom,
    VehicleRegistrationFacts Vehicle,
    IReadOnlyDictionary<string, MerchantCertificationView> MerchantByNit)
{
    public static readonly CertificationView Empty = new(
        null, null, null, null, VehicleRegistrationFacts.Empty,
        new Dictionary<string, MerchantCertificationView>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// ¿Hay al menos una celda de SOAT o RTM con dato? (D8.) Es la condición de emisión del
    /// certificado: el avalúo solo no basta — ese bloque va en el FUR.
    /// </summary>
    public bool HasSoatOrRtmData =>
        (Soat?.HasAnyValue ?? false) || (Rtm?.HasAnyValue ?? false);

    public MerchantCertificationView? Merchant(string? nit) =>
        !string.IsNullOrWhiteSpace(nit) && MerchantByNit.TryGetValue(nit.Trim(), out var value)
            ? value
            : null;
}

/// <summary>
/// Registro mercantil resuelto para un NIT, en las dos formas que necesita el expediente.
/// </summary>
/// <remarks>
/// <see cref="Fields"/> conserva la forma <c>rues_*</c> que ya consume el generador del certificado.
/// No es una concesión perezosa: ese documento imprime unos veinte campos, y modelarlos todos en el
/// canónico es el alcance de la HU #11306. Mientras tanto, el lector proyecta a esa forma lo que sí
/// está modelado y completa el resto con lo guardado antes del despliegue — de modo que el cambio
/// visible de esta HU sea exactamente uno: <b>desaparece la consulta en vivo</b>.
/// </remarks>
public sealed record MerchantCertificationView(
    MerchantRegistration? Canonical,
    IReadOnlyDictionary<string, string?> Fields,
    CertificationProvenance From)
{
    public string? Field(string key) => Fields.TryGetValue(key, out var value) ? value : null;

    /// <summary>Sin razón social no hay certificado que emitir.</summary>
    public bool CanBeCertified => !string.IsNullOrWhiteSpace(Field("rues_razon_social"));
}
