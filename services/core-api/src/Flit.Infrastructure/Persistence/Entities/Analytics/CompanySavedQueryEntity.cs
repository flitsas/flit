namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Una consulta guardada por un usuario de una empresa gestora.
///
/// <para><b>Por qué en la base y no en el navegador.</b> Guardarlas en <c>localStorage</c> sale
/// gratis y es justo lo que las volvería desechables: se pierden al cambiar de equipo o de
/// navegador, y una consulta que hay que volver a armar cada vez no llega a usarse. Además, las
/// fases siguientes —compartirlas con el equipo, programar su envío— necesitan que ya vivan aquí;
/// hacerlo al revés obliga a una migración de datos que nadie va a querer hacer.</para>
///
/// <para>El alcance es <c>empresa + usuario</c>. Es el gemelo de
/// <c>Admin.OtSavedQueryEntity</c>, que se alcanza por <c>organismo + usuario</c>: dos tablas y no
/// una porque lo que las nombra es distinto —una cita organismos y tipos de trámite de la empresa,
/// la otra cita empresas clientes y revisores del organismo— y una tabla común obligaría a una
/// columna de discriminación que solo sirve para volver a separarlas en cada consulta.</para>
/// </summary>
public sealed class CompanySavedQueryEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>
    /// La definición serializada (<c>QueryDefinition</c>). Se guarda como JSON y no en columnas
    /// porque el catálogo de campos crece: una tabla de condiciones obligaría a una migración cada
    /// vez que se agrega un campo consultable, que es exactamente lo que el catálogo evita.
    /// </summary>
    public string Definicion { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
