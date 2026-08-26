namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Empresa representada para la que un mandatario firma en un organismo concreto.
///
/// <para><b>No confundir con <see cref="MandateSignerCompany"/>.</b> Aquella lleva la COMPAÑÍA GESTORA
/// (el tenant) y responde "qué mandatarios tiene esta gestora en este organismo", que es la consulta
/// que hace el trámite para poblar la lista. Esta acota esa lista a las empresas concretas —las que se
/// dan de alta dentro del formulario del representante legal— para las que ese mandatario firma.</para>
///
/// <para><b>Ausencia = aplica a todas.</b> Un mandatario sin filas para un organismo sirve para
/// cualquier empresa allí. Es deliberado: sin esa regla, los mandatarios que ya existen desaparecerían
/// de todos los trámites al desplegar.</para>
/// </summary>
public sealed class MandateSignerRepresentedCompany
{
    public Guid Id { get; set; }
    public Guid MandateSignerId { get; set; }
    public Guid TransitOfficeId { get; set; }
    public Guid RepresentedCompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
