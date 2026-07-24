namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Fuente que originó el bloqueo registral CF-03 (HU #10877): doble fuente, independientes entre sí
/// (cualquiera de las dos bloquea).
/// </summary>
public enum VehicleStateSource
{
    /// <summary>El RUNT reporta el vehículo con estado "ACTIVO" (ya matriculado/circulando).</summary>
    Runt,

    /// <summary>El RUNT no respondió o no devolvió el estado del vehículo (dato no verificable).</summary>
    RuntDesconocido,

    /// <summary>En FLIT ya existe una matrícula APROBADA para el mismo VIN.</summary>
    Flit,
}

/// <summary>
/// CF-03 (HU #10877) — detalle del bloqueo "vehículo ya matriculado" (precondición registral):
/// bloqueo DURO, no subsanable (ni "riesgo aceptado" ni rol lo saltan). Viaja desde el orquestador
/// (preflight/gate de radicación) hasta el endpoint, que lo traduce a las extensions RFC7807 del
/// 422 <see cref="VehicleStatePolicy.ErrorCode"/>.
/// </summary>
public sealed record VehicleStateBlock(string VehicleStatus, string ProcedureType, VehicleStateSource Source);

/// <summary>
/// CF-03 (HU #10877) — constantes del gate de precondición registral: un trámite no puede iniciarse
/// ni radicarse si el estado registral del vehículo lo hace inválido (ejemplo canónico: Matrícula
/// Inicial sobre un vehículo ya matriculado). Aplica SOLO a la familia Matrícula Inicial.
/// </summary>
public static class VehicleStatePolicy
{
    /// <summary>Código de error 422: precondición registral inválida para el tipo de trámite.</summary>
    public const string ErrorCode = "VEHICLE_STATE_INVALID_FOR_TYPE";

    /// <summary><c>procedureType</c> del detalle RFC7807 para la familia Matrícula Inicial.</summary>
    public const string ProcedureTypeMatriculaInicial = "matricula_inicial";

    /// <summary><c>vehicleStatus</c> cuando el RUNT reporta el vehículo ACTIVO (AC1).</summary>
    public const string VehicleStatusActivoRunt = "ACTIVO";

    /// <summary><c>vehicleStatus</c> cuando el RUNT no responde o no trae el dato (AC3).</summary>
    public const string VehicleStatusDesconocido = "DESCONOCIDO";

    /// <summary><c>vehicleStatus</c> cuando la fuente del bloqueo es una matrícula APROBADA en FLIT (AC2).</summary>
    public const string VehicleStatusAprobadoFlit = "APROBADO_FLIT";
}
