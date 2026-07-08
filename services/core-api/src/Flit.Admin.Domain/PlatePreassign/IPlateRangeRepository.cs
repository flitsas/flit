namespace Flit.Admin.Domain.PlatePreassign;

/// <summary>Resumen de un rango de placas con su conteo de disponibles (Feature #10587).</summary>
public sealed record PlateRangeSummary(
    Guid Id,
    Guid TenantId,
    Guid TransitOfficeId,
    string Prefix,
    int RangeFrom,
    int RangeTo,
    DateTimeOffset EditableUntil,
    int TotalPlates,
    int AvailablePlates);

/// <summary>Placa individual del inventario con su estado (Feature #10587).</summary>
public sealed record PlateDetail(
    Guid Id,
    Guid PlateRangeId,
    Guid TenantId,
    Guid TransitOfficeId,
    string Plate,
    string State,
    Guid? ProcedureInstanceId);

/// <summary>Resultado de crear un rango: éxito o motivo del rechazo.</summary>
public sealed record CreatePlateRangeResult(bool Success, string? Error, Guid? RangeId, int PlatesCreated)
{
    public static CreatePlateRangeResult Ok(Guid rangeId, int plates) => new(true, null, rangeId, plates);

    public static CreatePlateRangeResult Fail(string error) => new(false, error, null, 0);
}

/// <summary>Resultado de una operación puntual sobre una placa o rango.</summary>
public sealed record PlateOpResult(bool Success, string? Error)
{
    public static readonly PlateOpResult Ok = new(true, null);

    public static PlateOpResult Fail(string error) => new(false, error);
}

/// <summary>
/// Inventario de rangos de placas de preasignación (Feature #10587). RLS permisiva a nivel BD;
/// la autorización (flag de la compañía + allow_plate_preassign del OT + grant) se aplica en la
/// capa de aplicación antes de invocar estas operaciones de escritura.
/// </summary>
public interface IPlateRangeRepository
{
    /// <summary>Crea un rango para una compañía (validado) y lo explota en placas disponibles.</summary>
    Task<CreatePlateRangeResult> CreateRangeAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string prefix,
        int rangeFrom,
        int rangeTo,
        Guid? createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Lista los rangos de una compañía (opcionalmente por OT) con su conteo.</summary>
    Task<IReadOnlyList<PlateRangeSummary>> ListRangesAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>Lista las placas de una compañía con filtros (OT, estado).</summary>
    Task<IReadOnlyList<PlateDetail>> ListDetailsAsync(
        Guid companyTenantId,
        Guid? transitOfficeId,
        string? state,
        CancellationToken cancellationToken = default);

    /// <summary>Resuelve el <c>transit_office_id</c> del tenant OT (perfil OT), o null si no aplica.</summary>
    Task<Guid?> ResolveOfficeIdAsync(Guid otTenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Se puede operar la preasignación entre esta compañía y este OT? Exige: flag de la compañía
    /// (<c>plate_preassign_enabled</c>) + grant vigente (compañía↔OT) + <c>allow_plate_preassign</c> del OT.
    /// </summary>
    Task<bool> IsAssignmentAllowedAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edita un rango dentro de la ventana de 60 min: revalida y re-explota las placas. Falla si la
    /// ventana venció o si alguna placa del rango ya está preasignada/utilizada.
    /// </summary>
    Task<CreatePlateRangeResult> EditRangeAsync(
        Guid rangeId,
        string prefix,
        int rangeFrom,
        int rangeTo,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el estado de una placa validando la máquina de estados (bloquear/desbloquear/revocar).
    /// </summary>
    Task<PlateOpResult> SetPlateStateAsync(
        Guid plateDetailId,
        string targetState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva una placa concreta para un trámite (disponible→preasignada) con guarda de concurrencia.
    /// Idempotente: si ya está preasignada para el mismo trámite, devuelve <c>true</c>. <c>false</c> si
    /// no existe o no está disponible (tomada por otro).
    /// </summary>
    Task<bool> TryReservePlateAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        string plate,
        Guid procedureInstanceId,
        CancellationToken cancellationToken = default);
}
