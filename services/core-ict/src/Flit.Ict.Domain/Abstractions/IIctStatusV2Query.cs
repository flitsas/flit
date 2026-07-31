namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Respuesta de estado v2-native (plan §A.9): el vocabulario del pre-trámite (<c>ictEstado</c>), la
/// correlación con el borrador materializado (<c>procedureInstanceId</c>) y, cuando existe, la proyección
/// del estado v2 del trámite en core-api (<c>tramiteStatus</c>: borrador/preparado/entregado/…). Sin códigos
/// numéricos v1 (esos siguen en el endpoint /status-process de compatibilidad).
/// </summary>
public sealed record IctStatusV2Response(
    string ManagerIdTransaction,
    string IctEstado,
    Guid? ProcedureInstanceId,
    string? TramiteStatus,
    string Comments);

/// <summary>Consulta del estado v2 de un pre-trámite por su manager_id_transaction (TransactionFlit).</summary>
public interface IIctStatusV2Query
{
    Task<IctStatusV2Response?> GetByManagerIdTransactionAsync(
        string managerIdTransaction,
        Guid tenantId,
        CancellationToken ct = default);
}
