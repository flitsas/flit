namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Una consulta guardada por SuperAdmin en modo «todas las compañías».
///
/// <para>Sin <c>TenantId</c> ni <c>UserId</c> de alcance a propósito: el gemelo de
/// <see cref="CompanySavedQueryEntity"/>, pero compartida entre TODO el equipo de SuperAdmin, no de
/// una compañía ni de una persona. <c>CreatedByUserId</c> es solo auditoría — cualquier SuperAdmin
/// puede editar o borrar la consulta de otro (ver <c>SuperAdminSavedQueryRepository</c>).</para>
/// </summary>
public sealed class SuperAdminSavedQueryEntity
{
    public Guid Id { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>La definición serializada (<c>QueryDefinition</c>), igual que en las demás consultas.</summary>
    public string Definicion { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
