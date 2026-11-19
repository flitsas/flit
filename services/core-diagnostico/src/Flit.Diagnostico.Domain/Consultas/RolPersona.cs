namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Rol de una persona del vehículo (<c>diagnostico.personas_vehiculo.rol</c>, HU #13790, F05-C3). En la base:
/// <c>PROPIETARIO</c> o <c>LOCATARIO</c>. Las celdas por propietario consultan a todas las personas con
/// <see cref="Propietario"/>; las de locatario, a todas las de <see cref="Locatario"/>.
/// </summary>
public enum RolPersona
{
    /// <summary>Propietario actual según el RUNT del vehículo.</summary>
    Propietario,

    /// <summary>Locatario del leasing: el del proveedor o, si el proveedor no trae ninguno, el que declaró el cliente.</summary>
    Locatario,
}
