namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Puerto para preguntarle al directorio de representantes legales de Admin (ADR-0033) si una compañía
/// —identificada por su NIT— tiene un representante <b>utilizable</b> para firmar un trámite
/// (HU #11195). Desacopla el módulo de trámites del de Admin, mismo patrón que
/// <see cref="ISignatureVaultPolicy"/> / <see cref="IIdentityValidationPolicy"/>.
///
/// <para><b>Qué significa "utilizable":</b> un representante ACTIVO de esa compañía que tenga a la vez
/// una <b>escritura vigente</b> que lo faculte y una <b>firma del baúl vigente o una validación de
/// identidad vigente</b>. Las dos condiciones son necesarias: la escritura acredita la representación
/// y la firma/identidad es el mecanismo con el que firma. Un representante con escritura vencida no
/// puede firmar aunque tenga la firma al día, y uno con la escritura al día tampoco puede si no tiene
/// con qué firmar.</para>
/// </summary>
public interface IRepresentanteLegalDirectory
{
    /// <summary>
    /// ¿La compañía <paramref name="nitCompania"/> tiene al menos un representante utilizable en
    /// <paramref name="hoy"/> (fecha calendario Colombia)? <paramref name="nitCompania"/> es PII
    /// (Ley 1581): no loguear.
    /// </summary>
    Task<bool> TieneRepresentanteUtilizableAsync(
        Guid tenantId,
        string nitCompania,
        DateOnly hoy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación inerte: responde SIEMPRE que la compañía sí tiene representante utilizable, con lo
/// que la compuerta de la HU #11195 nunca dispara y el comportamiento queda exactamente igual que
/// antes de esa HU. Es el default de los tests de dominio/aplicación que no la ejercitan: el default
/// seguro aquí es "no hacer nada de más", no "enviar de más" — un envío indebido le manda un correo de
/// validación biométrica a una persona real.
/// </summary>
public sealed class NullRepresentanteLegalDirectory : IRepresentanteLegalDirectory
{
    public static NullRepresentanteLegalDirectory Instance { get; } = new();

    public Task<bool> TieneRepresentanteUtilizableAsync(
        Guid tenantId,
        string nitCompania,
        DateOnly hoy,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
