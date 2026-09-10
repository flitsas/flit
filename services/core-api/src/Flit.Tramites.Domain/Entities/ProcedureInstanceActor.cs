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

    /// <summary>
    /// Posición del actor dentro de su rol/lado (ADR-0053, Múltiple Propietario): 1 = principal
    /// (el "solidario" que absorbe el residuo de porcentaje y el único que existía antes de
    /// ADR-0053), 2..4 = copropietarios agregados. Junto con <see cref="ProcedureInstanceId"/> y
    /// <see cref="ProcedureEntityId"/> forma la unicidad del actor (ver
    /// <c>uq_procedure_instance_actors_instance_entity_ordinal</c>).
    /// </summary>
    public int Ordinal { get; set; } = 1;

    /// <summary>
    /// Porcentaje de propiedad del actor sobre el lado (2 decimales). <c>NULL</c> cuando el rol
    /// tiene un solo actor (comportamiento previo a ADR-0053, sin bloque de reparto en la UI).
    /// Con 2+ actores por lado, la suma de los porcentajes efectivos debe ser exactamente 100 —
    /// invariante que se valida en <c>Flit.Tramites.Application</c>, no en un CHECK de fila
    /// (ver ADR-0053, Tradeoff aceptado).
    /// </summary>
    public decimal? OwnershipPercentage { get; set; }

    public string Metadata { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
    public ProcedureEntity? ProcedureEntity { get; set; }
}
