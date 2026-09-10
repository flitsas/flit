namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Qué decide si el contrato de mandato lleva bloque de firma del mandatario y de qué forma.
///
/// <para><b>Convenio.</b> Un acuerdo comercial entre la compañía gestora y el organismo. Con convenio
/// el mandato NO lleva bloque de firma del mandatario —sus datos siguen nombrados en el cuerpo del
/// contrato—; sin convenio sí lo lleva, porque el mandatario es un actor obligatorio del trámite.</para>
///
/// <para><b>No es el grant compañía↔OT.</b> Aquel es el permiso para radicar y la radicación lo exige,
/// así que todo trámite radicado lo tiene: usarlo como convenio dejaría todos los mandatos sin firma de
/// mandatario y el caso contrario no se daría nunca.</para>
///
/// <para><b>Resguardo.</b> <see cref="OrganismoSinNingunConvenio"/> distingue "esta compañía no tiene
/// convenio" de "este organismo no trabaja con convenios". Sin esa distinción, un organismo al que
/// todavía nadie le ha registrado convenios se comportaría igual que uno con convenio, y el mandato
/// saldría sin el actor obligatorio.</para>
/// </summary>
/// <param name="TieneConvenio">La compañía del trámite tiene convenio activo con el organismo.</param>
/// <param name="OrganismoSinNingunConvenio">El organismo no tiene convenio activo con NINGUNA compañía.</param>
/// <param name="FirmaFisica">
/// El mandatario elegido firma a mano ante ese organismo: se conserva el bloque, pero con la línea de
/// guiones bajos y sin estampa.
/// </param>
public sealed record MandatoFirmaContexto(
    bool TieneConvenio,
    bool OrganismoSinNingunConvenio,
    bool FirmaFisica);

/// <summary>
/// Puerto para resolver el contexto de firma del mandato (convenio + firma física). Desacopla el módulo
/// de trámites del de Admin, igual que <see cref="IMandateRequirementPolicy"/> y
/// <see cref="ISignatureVaultPolicy"/>.
/// </summary>
public interface IMandatoFirmaPolicy
{
    /// <summary>
    /// Contexto de firma para la compañía <paramref name="companyTenantId"/> en el organismo
    /// <paramref name="transitOfficeId"/>, con el mandatario <paramref name="mandateSignerId"/> si ya
    /// está elegido.
    /// </summary>
    Task<MandatoFirmaContexto> ResolveAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        Guid? mandateSignerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default seguro: sin convenio, organismo sin convenios y sin firma física ⇒ el mandato conserva el
/// bloque de firma del mandatario, que es el comportamiento previo a esta regla. Un default que
/// suprimiera el bloque haría desaparecer un actor obligatorio en cuanto el puerto no estuviera cableado.
/// </summary>
public sealed class NullMandatoFirmaPolicy : IMandatoFirmaPolicy
{
    public static NullMandatoFirmaPolicy Instance { get; } = new();

    public Task<MandatoFirmaContexto> ResolveAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        Guid? mandateSignerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MandatoFirmaContexto(false, true, false));
}
