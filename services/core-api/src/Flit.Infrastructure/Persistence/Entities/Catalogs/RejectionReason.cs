namespace Flit.Infrastructure.Persistence.Entities.Catalogs;

/// <summary>
/// Causal de rechazo del catálogo global, administrada por SuperAdmin.
///
/// <para>Es lo que sustituye al motivo escrito a mano: el revisor del organismo marca una o varias
/// causales de esta lista y, aparte, escribe una observación general. Marcar varias es válido — un
/// expediente puede llegar con improntas borrosas, sin impronta y sin pago de impuestos a la vez, y
/// el gestor necesita saberlo todo para subsanar.</para>
///
/// <para>El catálogo es GLOBAL a propósito: si cada organismo definiera el suyo, veinte organismos
/// inventarían veinte formas de decir «improntas borrosas» y el reporte dejaría de ser comparable
/// entre organismos y entre empresas, que es justo para lo que existe.</para>
/// </summary>
public sealed class RejectionReason
{
    public Guid Id { get; set; }

    /// <summary>
    /// Código estable. Es la llave de siembra y de los reportes: la descripción puede reescribirse
    /// sin romper las series históricas.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Modalidad a la que aplica (<c>matricula_inicial</c> | <c>traspaso</c>). Las causales no son
    /// intercambiables: «manifiesto de aduana» no aplica a un traspaso ni «escritura del vendedor»
    /// a una matrícula inicial.
    /// </summary>
    public string Modalidad { get; set; } = string.Empty;

    /// <summary>Orden de presentación en el modal de rechazo.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Baja lógica y no borrado: una causal retirada debe seguir resolviendo el nombre de los
    /// rechazos históricos que la usaron.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
