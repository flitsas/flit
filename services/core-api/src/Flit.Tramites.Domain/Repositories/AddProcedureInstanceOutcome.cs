namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Resultado del insert resiliente de una instancia de trámite
/// (<see cref="IProcedureInstanceRepository.AddWithUniqueReferenceAsync"/>).
/// </summary>
public enum AddProcedureInstanceOutcome
{
    /// <summary>Insert exitoso.</summary>
    Created,

    /// <summary>
    /// El radicado chocó contra el índice único global <c>uq_procedure_instances_reference</c>
    /// → mapear a 409. Desde la HU #12151 lo asigna una secuencia, así que por la ruta normal de
    /// creación no puede ocurrir; queda para un INSERT que fije el radicado a mano.
    /// </summary>
    ReferenceConflict,

    /// <summary>
    /// El insert violó una foreign key: el <c>tenant_id</c>, el <c>created_by_user_id</c>
    /// o el <c>procedure_type_id</c> no existen. Mapear a 422 (entrada inválida), no 500.
    /// </summary>
    ReferencedEntityMissing,
}
