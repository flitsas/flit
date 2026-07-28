using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Validation;
using Flit.Ict.Grpc.Contracts;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Cliente real de consulta a fuentes externas: delega en core-api por gRPC (IctConsultation.Query),
/// que reutiliza los proveedores RUNT/SOAT/RTM/RNMC/conductor (Verifik/Kyverum) con su modo mock|real
/// y credenciales. Mapea la respuesta normalizada a <see cref="ConsultationResult"/> (los 5 hechos que
/// aplican los validadores). Si core-api reporta error, se propaga para que el orquestador reintente
/// (fail-closed: no se materializa el borrador sin una respuesta real de las fuentes).
/// </summary>
public sealed class IctGrpcConsultationClient(IctConsultation.IctConsultationClient client)
    : IConsultationClient
{
    public async Task<ConsultationResult> QueryAsync(
        Guid tenantId,
        string queryType,
        string plate,
        string vin,
        string documentType,
        string documentNumber,
        CancellationToken ct = default)
    {
        var reply = await client.QueryAsync(
            new ConsultationRequest
            {
                TenantId = tenantId.ToString(),
                QueryType = queryType ?? string.Empty,
                Plate = plate ?? string.Empty,
                Vin = vin ?? string.Empty,
                DocumentType = documentType ?? string.Empty,
                DocumentNumber = documentNumber ?? string.Empty,
            },
            cancellationToken: ct);

        if (!string.IsNullOrEmpty(reply.ErrorCode))
        {
            throw new InvalidOperationException($"Consulta de fuentes falló en core-api: {reply.ErrorCode}");
        }

        return new ConsultationResult(
            SoatStatus: string.IsNullOrEmpty(reply.SoatStatus) ? null : reply.SoatStatus,
            RtmStatus: string.IsNullOrEmpty(reply.RtmStatus) ? null : reply.RtmStatus,
            VehicleModelYear: reply.VehicleModelYear > 0 ? reply.VehicleModelYear : null,
            HasActiveSanctions: reply.HasActiveSanctions,
            PazYSalvo: reply.PazYSalvoKnown ? reply.PazYSalvo : null,
            TransitOfficeName: string.IsNullOrEmpty(reply.TransitOfficeName) ? null : reply.TransitOfficeName);
    }
}
