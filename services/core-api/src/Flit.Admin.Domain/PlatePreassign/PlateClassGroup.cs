namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>
/// HU8 [BACKEND] (Caso C 2026-11-09, D1) — grupo de placa según la clase del vehículo. Decide qué formato
/// exige la asignación y la corrección de placa del OT (<see cref="PlateRangeRules.IsValidPlateForGroup"/>).
/// Sin grupo (<c>null</c>): clase vacía, «SIN CLASE» o desconocida ⇒ unión de formatos actual.
/// Valor de contrato: <see cref="PlateRangeRules.ToContractValue"/> (<c>CARRO</c> | <c>MOTO</c> |
/// <c>REMOLQUE</c> | <c>MAQUINARIA</c>).
/// </summary>
public enum PlateClassGroup
{
    /// <summary>Carro: <c>^[A-Z]{3}[0-9]{3}$</c> (ABC123).</summary>
    Carro,

    /// <summary>Moto y afines: <c>^[A-Z]{3}[0-9]{2}[A-Z]?$</c> (ABC12D o ABC12).</summary>
    Moto,

    /// <summary>Remolque / semirremolque: <c>^[RS][0-9]{5,6}$</c> (R12345, S654321).</summary>
    Remolque,

    /// <summary>Maquinaria agrícola y de construcción: <c>^[A-Z]{2}[0-9]{6}$</c> (MC029554).</summary>
    Maquinaria,
}
