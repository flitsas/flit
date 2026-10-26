namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Filtros opcionales del historial de auditoría de gobernanza de un tenant (HU #14004).
/// Todos se combinan con AND. Los textos son «contiene» sin distinguir mayúsculas.
/// </summary>
/// <param name="ChangedFromInclusive">Instante inicial inclusivo (<c>changed_at &gt;=</c>).</param>
/// <param name="ChangedToExclusive">Instante final exclusivo (<c>changed_at &lt;</c>).</param>
/// <param name="Field">Contiene en el campo modificado (<c>field_name</c> o <c>entity_name</c>).</param>
/// <param name="OldValue">Contiene en el valor anterior.</param>
/// <param name="Operator">Contiene en el operador: id de usuario, nombre o correo.</param>
public sealed record TenantAuditLogFilter(
    DateTimeOffset? ChangedFromInclusive = null,
    DateTimeOffset? ChangedToExclusive = null,
    string? Field = null,
    string? OldValue = null,
    string? Operator = null)
{
    public static TenantAuditLogFilter None { get; } = new();
}
