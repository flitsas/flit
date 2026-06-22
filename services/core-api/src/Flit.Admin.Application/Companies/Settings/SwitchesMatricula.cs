namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Switches de matrícula del tenant (RF07). Usado tanto en el request como en la
/// respuesta de configuración. Claves JSON: <c>allowInitialRegistration</c>,
/// <c>allowMiscNewVehicles</c>, <c>onlyOwnVehicles</c>.
/// </summary>
public sealed record SwitchesMatricula(
    bool AllowInitialRegistration,
    bool AllowMiscNewVehicles,
    bool OnlyOwnVehicles);
