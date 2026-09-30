namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13131 (ADR-0061, retiro de la firma física fase 2) — un mandatario que depende SOLO de la firma
/// física ante un organismo: activo, con <c>signs_physically</c> verdadero en ese organismo, sin firma del
/// baúl y sin validación biométrica aprobada y vigente. <b>No lleva documento ni correo</b> (PII, Ley 1581):
/// el reporte identifica al mandatario por su id y su nombre.
/// </summary>
/// <param name="MandateSignerId">Id del mandatario.</param>
/// <param name="FullName">Nombre del mandatario.</param>
/// <param name="CompanyTenantId">Compañía gestora vinculada en el organismo; <c>null</c> si no tiene ninguna (registro anterior a la acotación).</param>
/// <param name="CompanyName">Razón social de la compañía; <c>null</c> sin compañía.</param>
/// <param name="TransitOfficeId">Organismo de tránsito donde firma a mano.</param>
/// <param name="TransitOfficeCode">Código del organismo.</param>
/// <param name="TransitOfficeName">Nombre del organismo.</param>
/// <param name="CurrentSignatureForm">Forma con la que firma hoy: siempre <see cref="PhysicalSignatureMigrationForms.FirmaFisica"/>.</param>
/// <param name="DeclaredSignatureMethod">Forma de firma declarada en la ficha (<c>baul</c> | <c>biometria</c>) o <c>null</c> si no la declaró.</param>
/// <param name="MissingData">Dato que le falta para migrar: uno de <see cref="PhysicalSignatureMigrationMissing"/>.</param>
public sealed record PhysicalSignatureMigrationRow(
    Guid MandateSignerId,
    string FullName,
    Guid? CompanyTenantId,
    string? CompanyName,
    Guid TransitOfficeId,
    string TransitOfficeCode,
    string TransitOfficeName,
    string CurrentSignatureForm,
    string? DeclaredSignatureMethod,
    string MissingData);

/// <summary>Valores de <see cref="PhysicalSignatureMigrationRow.CurrentSignatureForm"/>.</summary>
public static class PhysicalSignatureMigrationForms
{
    public const string FirmaFisica = "firma_fisica";
}

/// <summary>Valores de <see cref="PhysicalSignatureMigrationRow.MissingData"/>.</summary>
public static class PhysicalSignatureMigrationMissing
{
    /// <summary>No declaró forma de firma: le falta la firma del baúl o la validación biométrica.</summary>
    public const string BaulOBiometria = "firma_baul_o_validacion_biometrica";

    /// <summary>Declaró baúl y no tiene firma activa y vigente en el baúl.</summary>
    public const string FirmaBaul = "firma_baul";

    /// <summary>Declaró biometría y no tiene ninguna validación aprobada.</summary>
    public const string ValidacionBiometrica = "validacion_biometrica";

    /// <summary>Tiene validación aprobada pero ya pasó la ventana de 30 días: falta renovarla.</summary>
    public const string ValidacionBiometricaVigente = "validacion_biometrica_vigente";
}

/// <summary>
/// HU #13131 — puerto del reporte de migración de la firma física. Solo lectura, sin alcance de tenant:
/// lo consume únicamente el Super Admin (la restricción la impone el endpoint, que responde 403 al resto).
/// </summary>
public interface IPhysicalSignatureMigrationReader
{
    /// <summary>
    /// Mandatarios activos que dependen solo de la firma física. <paramref name="transitOfficeId"/> filtra
    /// por organismo. Un mandatario al que se le vincula una firma del baúl o se le aprueba la validación
    /// biométrica sale del resultado en la siguiente consulta.
    /// </summary>
    Task<IReadOnlyList<PhysicalSignatureMigrationRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}
