namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Por qué no se pudo asignar una placa a un trámite.
///
/// <para>La operación devolvía <c>null</c> en cuatro puntos distintos y el endpoint los colapsaba en
/// un único 422 que enumeraba las causas con "o". El caso frecuente en operación —el OT escribe una
/// placa que ya se asignó antes— llegaba al usuario como un formulario que simplemente no avanzaba,
/// sin decir qué pasaba. Cada causa se nombra para poder explicarla.</para>
/// </summary>
public enum PlateAssignmentFailure
{
    /// <summary>Sin fallo: la asignación se completó.</summary>
    None = 0,

    /// <summary>Falta la placa o el inventario de rangos no está disponible.</summary>
    MissingPlate,

    /// <summary>El trámite no existe o el OT no tiene grant vigente para verlo.</summary>
    ProcedureNotAccessible,

    /// <summary>El trámite existe pero no está entregado y en sub-estado <c>preasignado</c>.</summary>
    NotPreassigned,

    /// <summary>La placa ya está registrada para este OT (asignada a otro trámite o reservada).</summary>
    PlateAlreadyAssigned,

    /// <summary>La placa no pertenece a ningún rango disponible del OT (y no se pidió fuera de rango).</summary>
    PlateNotAvailable,

    /// <summary>
    /// La placa ya figura en otro trámite vivo del sistema (cualquier compañía u OT). Distinto de
    /// <see cref="PlateAlreadyAssigned"/>, que habla del inventario de rangos de este OT.
    /// </summary>
    PlateInUseByAnotherProcedure,
}

/// <summary>
/// Resultado de asignar una placa: el trámite actualizado, o la causa concreta del fallo.
/// </summary>
public sealed record PlateAssignmentOutcome(
    OtClientProcedure? Procedure,
    PlateAssignmentFailure Failure,
    string? Detail = null)
{
    public static PlateAssignmentOutcome Ok(OtClientProcedure procedure) =>
        new(procedure, PlateAssignmentFailure.None);

    public static PlateAssignmentOutcome Fail(PlateAssignmentFailure failure, string? detail = null) =>
        new(null, failure, detail);

    public bool Succeeded => Failure == PlateAssignmentFailure.None && Procedure is not null;
}
