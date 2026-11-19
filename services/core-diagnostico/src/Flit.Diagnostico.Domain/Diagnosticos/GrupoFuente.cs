using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>
/// Grupo del alcance al que pertenece cada fuente (HU #13750, F01-H4, R-24): «Por Vehículo» (RUNT, SIMIT_VEHICULO, AVALUO,
/// IMPUESTO_DEPARTAMENTAL), «Por Propietario» (RUNT_PROPIETARIO, SIMIT_PROPIETARIO) y «Por Locatario» (RUNT_LOCATARIO,
/// SIMIT_LOCATARIO). Una fuente declarada en un grupo que no es el suyo es un alcance inválido (422).
/// </summary>
public enum GrupoFuente
{
    PorVehiculo,
    PorPropietario,
    PorLocatario,
}

/// <summary>Grupo de cada <see cref="Fuente"/>.</summary>
public static class GruposFuente
{
    public static GrupoFuente De(Fuente fuente) => fuente switch
    {
        Fuente.Runt or Fuente.SimitVehiculo or Fuente.Avaluo or Fuente.ImpuestoDepartamental => GrupoFuente.PorVehiculo,
        Fuente.RuntPropietario or Fuente.SimitPropietario => GrupoFuente.PorPropietario,
        Fuente.RuntLocatario or Fuente.SimitLocatario => GrupoFuente.PorLocatario,
        _ => throw new ArgumentOutOfRangeException(nameof(fuente), fuente, "Fuente sin grupo."),
    };
}
