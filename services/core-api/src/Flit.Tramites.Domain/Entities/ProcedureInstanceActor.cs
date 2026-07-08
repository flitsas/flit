namespace Flit.Tramites.Domain.Entities;

public sealed class ProcedureInstanceActor
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public Guid ProcedureEntityId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    /// Tipo de persona del actor (HU #10542): <c>natural</c> | <c>juridical</c> | <c>null</c>.
    /// Para persona natural, el documento de identidad se incorpora desde la validación
    /// biométrica y no se exige carga manual de cédula. <c>null</c> = sin distinguir (legacy).
    /// </summary>
    public string? PersonType { get; set; }

    /// <summary>
    /// Marca al actor como representante legal vendedor (HU #10544), para trazabilidad de la
    /// reutilización de la validación de identidad vigente (30 días). No altera el cálculo de
    /// vigencia, que es uniforme para todas las partes.
    /// </summary>
    public bool EsRepresentanteLegal { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
    public ProcedureEntity? ProcedureEntity { get; set; }
}
