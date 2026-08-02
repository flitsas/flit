namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Convenio comercial entre una compañía gestora y un organismo de tránsito.
///
/// <para><b>No confundir con <see cref="TenantTransitOfficeGrant"/>.</b> El grant es el PERMISO para
/// radicar, y la radicación lo exige. Si el convenio fuera el grant, todo trámite radicado tendría
/// convenio por construcción y la regla «sin convenio ⇒ el mandatario firma» no podría darse nunca.</para>
///
/// <para>Su único efecto hoy es documental: con convenio, el contrato de mandato NO lleva bloque de
/// firma del mandatario (los datos del actor siguen en el cuerpo del contrato); sin convenio, sí lo
/// lleva, porque el mandatario es un actor obligatorio del trámite.</para>
///
/// <para>Tabla propia y no una columna del grant porque revocar el permiso BORRA la fila del grant, y
/// eso se llevaría por delante el acuerdo comercial.</para>
/// </summary>
public sealed class CompanyTransitOfficeAgreement
{
    public Guid Id { get; set; }

    /// <summary>Compañía gestora (tenant).</summary>
    public Guid CompanyTenantId { get; set; }

    public Guid TransitOfficeId { get; set; }

    /// <summary>
    /// Baja lógica: retirar el convenio conserva la traza de que existió, que es lo que explica por qué
    /// un mandato emitido en su momento salió sin bloque de mandatario.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
