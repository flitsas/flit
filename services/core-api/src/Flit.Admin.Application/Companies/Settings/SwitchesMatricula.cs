namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Flag «solo vehículos propios» por familia de trámite
/// (<c>MATRICULAS</c> | <c>TRASPASO</c> | <c>OTROS</c>).
/// </summary>
public sealed record OnlyOwnVehiclesByFamily(
    bool Matriculas,
    bool Traspaso,
    bool Otros);

/// <summary>
/// Bloqueo de creación por familia. <c>true</c> = no permitir crear trámites de esa familia.
/// MATRICULAS en wire = invertido de <c>allowInitialRegistration</c>.
/// </summary>
public sealed record BlockProcedureFamily(
    bool Matriculas,
    bool Traspaso,
    bool Otros);

/// <summary>
/// Switches de matrícula del tenant (RF07). Usado tanto en el request como en la
/// respuesta de configuración. Claves JSON: <c>allowInitialRegistration</c>,
/// <c>allowMiscNewVehicles</c>, <c>onlyOwnVehicles</c> (legado = TRASPASO),
/// <c>onlyOwnVehiclesByFamily</c>, <c>blockProcedureFamily</c>.
/// </summary>
public sealed record SwitchesMatricula(
    bool AllowInitialRegistration,
    bool AllowMiscNewVehicles,
    bool OnlyOwnVehicles,
    OnlyOwnVehiclesByFamily? OnlyOwnVehiclesByFamily = null,
    BlockProcedureFamily? BlockProcedureFamily = null);
