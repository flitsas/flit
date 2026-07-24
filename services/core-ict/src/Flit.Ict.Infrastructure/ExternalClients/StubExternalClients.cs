using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Domain.Validation;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Stub de consulta a fuentes externas para dev/local (devuelve datos válidos). En producción se
/// reemplaza por el cliente gRPC hacia core-api que reutiliza los proveedores RUNT/SIMIT/RNMC.
/// TODO(ICT-CONSULT): cliente gRPC real hacia core-api (ConsultationGrpcService).
/// </summary>
public sealed class StubConsultationClient : IConsultationClient
{
    public Task<ConsultationResult> QueryAsync(
        Guid tenantId,
        string queryType,
        string plate,
        string vin,
        string documentType,
        string documentNumber,
        CancellationToken ct = default) =>
        Task.FromResult(new ConsultationResult(
            SoatStatus: "VIGENTE",
            RtmStatus: "VIGENTE",
            VehicleModelYear: null,
            HasActiveSanctions: false,
            PazYSalvo: true));
}

/// <summary>
/// Cliente de creación de borrador pendiente de HU4. Devuelve 'grpc_unavailable' para que el job de
/// envío NO marque novedad y el pre-trámite espere a que la implementación gRPC real esté disponible.
/// </summary>
public sealed class PendingProcedureDraftClient : IProcedureDraftClient
{
    public Task<CreateDraftResult> CreateDraftAsync(
        ExternalIntegrationMaster master,
        string procedureTypeCode,
        CancellationToken ct = default) =>
        Task.FromResult(new CreateDraftResult(null, null, null, "grpc_unavailable"));

    public Task<DraftActionResult> PauseDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        bool paused,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default) =>
        Task.FromResult(new DraftActionResult(null, "grpc_unavailable"));

    public Task<DraftActionResult> AbortDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default) =>
        Task.FromResult(new DraftActionResult(null, "grpc_unavailable"));
}
