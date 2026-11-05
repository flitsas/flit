namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>
/// Fuente de consulta de un diagnóstico (plan §5, enum de <c>consultas_fuente</c>; R-24 suma la RUNT de persona). En la base
/// se guarda con el código en mayúsculas y guion bajo (<see cref="RuntPropietario"/> → <c>RUNT_PROPIETARIO</c>). La usan
/// la configuración por compañía (HU #13736) y las celdas de consulta (F03).
/// </summary>
public enum Fuente
{
    /// <summary>RUNT del vehículo, por placa (Intempo) o por VIN.</summary>
    Runt,

    /// <summary>RUNT persona del propietario (<c>ConsultarConductor</c>).</summary>
    RuntPropietario,

    /// <summary>RUNT persona del locatario declarado o hidratado.</summary>
    RuntLocatario,

    /// <summary>SIMIT por placa.</summary>
    SimitVehiculo,

    /// <summary>SIMIT por documento del propietario.</summary>
    SimitPropietario,

    /// <summary>SIMIT por documento del locatario.</summary>
    SimitLocatario,

    /// <summary>Avalúo comercial (<c>ConsultarAvaluos</c>).</summary>
    Avaluo,

    /// <summary>Impuesto vehicular departamental (solo con la consulta automatizada, R-9).</summary>
    ImpuestoDepartamental,
}
