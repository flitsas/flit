namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Mapeo del <c>transaction_type</c> v1 (1-16) al <c>procedure_type_code</c> de core-api v2.
/// Catálogo global (sin RLS). Los tipos aún no publicados en v2 quedan con
/// <see cref="IsPublished"/> = false (materialización devuelve modalidad_not_available).
/// <para>ADR-0050: además del código, la fila declara lo que la materialización necesita saber del
/// tipo —familia y las dos capacidades que antes se decidían por número de transacción quemado en
/// C#—. ICT no ve el catálogo de core-api; este mapeo es el catálogo que sí gobierna.</para>
/// </summary>
public sealed class ProcedureTypeMapping : AuditableEntity
{
    public short ExternalTransactionType { get; set; }

    public string ProcedureTypeCode { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public string? Description { get; set; }

    /// <summary>Familia del tipo en v2: <c>MATRICULAS</c>, <c>TRASPASO</c> u <c>OTROS</c>.</summary>
    public string Family { get; set; } = "OTROS";

    /// <summary>El borrador lleva valor y fecha de venta.</summary>
    public bool RequiresCommercialValue { get; set; }

    /// <summary>
    /// El organismo de tránsito sale del nombre que devolvió el RUNT (paridad v1 de traspaso) y no
    /// lo asigna el gestor al recibir el borrador.
    /// </summary>
    public bool ResolvesTransitOfficeFromRunt { get; set; }
}
