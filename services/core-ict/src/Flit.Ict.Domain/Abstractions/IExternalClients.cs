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
/// Lo que la materialización necesita saber del tipo de trámite destino, tomado de
/// <c>ict.procedure_type_mapping</c> (ADR-0050). Antes el cliente gRPC lo deducía del propio código
/// —<c>Contains("TRASPASO")</c>— y del número de transacción, así que un tipo nuevo de la familia
/// TRASPASO cuyo código no dijera «traspaso» perdía el organismo del RUNT en silencio.
/// </summary>
public sealed record DraftProcedureType(
    string Code,
    string Family,
    bool RequiresCommercialValue,
    bool ResolvesTransitOfficeFromRunt);

/// <summary>
/// Cliente de orquestación hacia core-api (gRPC). Materializa el pre-trámite validado como un
/// borrador reutilizando los casos de uso de core-api, y opera su ciclo de vida (pausar/anular,
/// servicios v1 pauseDraftProcess/abortProcess).
/// </summary>
public interface IProcedureDraftClient
{
    /// <param name="vehicle">
    /// Bug #13304 — consulta RUNT de la validación ICT (vigente) que core-api reutiliza sin re-consultar
    /// (campo 14 <c>precomputed_vehicle</c>). <c>null</c> si el pre-trámite no tiene consulta de vehículo.
    /// </param>
    Task<CreateDraftResult> CreateDraftAsync(
        ExternalIntegrationMaster master,
        DraftProcedureType procedureType,
        VehicleConsultationSnapshot? vehicle,
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

    /// <summary>
    /// Bug #13304 — actualiza el precio de venta (datos comerciales) del borrador ya materializado en
    /// core-api. SOLO procede mientras el trámite esté en borrador. <c>Error</c> es un código estable:
    /// <c>not_draft</c> (ya avanzó), <c>not_found</c> (no existe en el tenant o el external_ref no coincide),
    /// <c>invalid_*</c> (validación del comercial) o <c>grpc_unavailable</c> (canal caído).
    /// </summary>
    Task<(bool Ok, string? Error)> UpdateCommercialAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        Guid externalRef,
        decimal sellingPrice,
        CancellationToken ct = default);
}

/// <summary>
/// Bug #13304 — qué sabe el pre-trámite de su consulta RUNT de vehículo. <see cref="RequiresVehicle"/> es
/// true si el master tiene una source_query VEHICLE/VIN (traspaso/matrícula); <see cref="Snapshot"/> es la
/// respuesta más reciente con resultado completo guardado (null si no hay).
/// </summary>
public sealed record VehicleSnapshotLookup(bool RequiresVehicle, VehicleConsultationSnapshot? Snapshot);

/// <summary>
/// Bug #13304 — lee y purga el resultado completo de la consulta RUNT guardado por el orquestador en
/// <c>ict.external_integration_source_response.vehicle_snapshot</c> (PII: solo vive hasta materializar).
/// </summary>
public interface IIctVehicleSnapshotReader
{
    /// <summary>Última respuesta VEHICLE/VIN consultada del master que tenga resultado completo.</summary>
    Task<VehicleSnapshotLookup> GetLatestAsync(Guid masterId, CancellationToken ct = default);

    /// <summary>Vacía <c>vehicle_snapshot</c> de todas las respuestas del master (minimización de PII).</summary>
    Task<int> PurgeAsync(Guid masterId, CancellationToken ct = default);

    /// <summary>
    /// Bug #13304 — re-encola la consulta VEHICLE/VIN del master (nueva source_query pendiente, copia de la
    /// última) para que el orquestador la resuelva. Como máximo UNA re-consulta por master dentro de la
    /// ventana de <paramref name="windowHours"/>: devuelve false (sin insertar) si ya se usó.
    /// </summary>
    Task<bool> RequeueVehicleQueryAsync(Guid masterId, int windowHours, CancellationToken ct = default);
}
