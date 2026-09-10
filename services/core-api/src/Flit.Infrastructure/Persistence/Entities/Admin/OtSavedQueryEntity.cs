namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Una consulta guardada por un usuario del organismo.
///
/// <para><b>Por qué en la base y no en el navegador.</b> Guardarlas en <c>localStorage</c> sale
/// gratis y es justo lo que las volvería desechables: se pierden al cambiar de equipo o de
/// navegador, y una consulta que hay que volver a armar cada vez no llega a usarse. Además, las
/// fases siguientes —compartirlas con el equipo, programar su envío— necesitan que ya vivan aquí;
/// hacerlo al revés obliga a una migración de datos que nadie va a querer hacer.</para>
///
/// <para>El alcance es <c>organismo + usuario</c>, no el tenant: el mismo usuario mirando dos
/// organismos distintos tiene dos juegos de consultas, porque las empresas y los revisores que
/// nombran son de un organismo concreto.</para>
/// </summary>
public sealed class OtSavedQueryEntity
{
    public Guid Id { get; set; }

    public Guid TransitOfficeId { get; set; }

    public Guid UserId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>
    /// La definición serializada (<c>OtQueryDefinition</c>). Se guarda como JSON y no en columnas
    /// porque el catálogo de campos crece: una tabla de condiciones obligaría a una migración cada
    /// vez que se agrega un campo consultable, que es exactamente lo que el catálogo evita.
    /// </summary>
    public string Definicion { get; set; } = "{}";

    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
