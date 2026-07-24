using Flit.Ict.Domain.Entities;
using Flit.Ict.Domain.Validation;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Consulta a fuentes externas (RUNT/SIMIT/RNMC...). En core-ict se delega a core-api por gRPC para
/// reutilizar los proveedores (Verifik/Kyverum) y sus credenciales; en dev, un stub devuelve datos válidos.
/// </summary>
public interface IConsultationClient
{
    Task<ConsultationResult> QueryAsync(
        Guid tenantId,
        string queryType,
        string plate,
        string vin,
        string documentType,
        string documentNumber,
        CancellationToken ct = default);
}

/// <summary>Resultado de materializar el borrador en core-api.</summary>
public sealed record CreateDraftResult(
    Guid? ProcedureInstanceId,
    string? ReferenceNumber,
    string? Status,
    string? ErrorCode);

/// <summary>Resultado de una acción de ciclo de vida (pausar/anular) sobre el borrador en core-api.</summary>
public sealed record DraftActionResult(string? Status, string? ErrorCode);

/// <summary>
/// Cliente de orquestación hacia core-api (gRPC). Materializa el pre-trámite validado como un
/// borrador reutilizando los casos de uso de core-api, y opera su ciclo de vida (pausar/anular,
/// servicios v1 pauseDraftProcess/abortProcess).
/// </summary>
public interface IProcedureDraftClient
{
    Task<CreateDraftResult> CreateDraftAsync(
        ExternalIntegrationMaster master,
        string procedureTypeCode,
        CancellationToken ct = default);

    /// <summary>Pausa o reanuda un borrador ya materializado (v1 pauseDraftProcess).</summary>
    Task<DraftActionResult> PauseDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        bool paused,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default);

    /// <summary>Anula un borrador (o rechazado) ya materializado (v1 abortProcess).</summary>
    Task<DraftActionResult> AbortDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default);
}
