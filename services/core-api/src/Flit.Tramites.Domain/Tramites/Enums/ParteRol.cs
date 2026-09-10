namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// Roles posibles de las partes intervinientes en un trámite (paridad <c>ParteRol</c> de Johan).
/// El modelo deja los roles restantes para futura extensión; hoy se configuran
/// <see cref="Comprador"/>, <see cref="Vendedor"/> y <see cref="Locatario"/>.
/// </summary>
public enum ParteRol
{
    Comprador,
    Vendedor,
    Heredero,
    Adjudicatario,
    RepresentanteLegal,
    Importador,

    /// <summary>
    /// Arrendatario del leasing (<c>LESSEE</c> en <c>tramites.procedure_entities</c>).
    ///
    /// <para>El resto del sistema ya lo trataba como parte real —el FUR le arma su
    /// <c>DocumentParte</c>, el resolver de destinatarios le manda los correos de estado y el ciclo de
    /// vida mapea <c>locatario</c> a <c>LESSEE</c>—, pero no estaba en este enum: <c>ParseRol</c> lo
    /// rechazaba con <c>invalid_rol</c>, así que ningún actor podía persistirse con ese rol y la parte
    /// del FUR era código inalcanzable. En su ausencia, <c>FurTramiteObservation</c> cae al comprador,
    /// y por eso la matrícula por leasing salía sin su observación obligatoria del párrafo 23.</para>
    ///
    /// <para>NO es sujeto de biometría ni de firma: en el leasing quien autoriza y firma es el
    /// propietario (la entidad financiera). El locatario se identifica y se notifica, nada más.</para>
    /// </summary>
    Locatario,
}
