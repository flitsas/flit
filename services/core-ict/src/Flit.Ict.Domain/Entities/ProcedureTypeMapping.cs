namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Mapeo del <c>transaction_type</c> v1 (1-16) al <c>procedure_type_code</c> de core-api v2.
/// Catálogo global (sin RLS). Los tipos aún no publicados en v2 quedan con
/// <see cref="IsPublished"/> = false (materialización devuelve modalidad_not_available).
/// </summary>
public sealed class ProcedureTypeMapping : AuditableEntity
{
    public short ExternalTransactionType { get; set; }

    public string ProcedureTypeCode { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public string? Description { get; set; }
}
