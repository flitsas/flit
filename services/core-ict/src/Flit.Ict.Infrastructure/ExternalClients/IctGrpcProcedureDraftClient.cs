using System.Globalization;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Grpc.Contracts;
using Grpc.Core;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Materializa el borrador en core-api vía gRPC (IctOrchestration.CreateDraftFromIct), reutilizando
/// los casos de uso de core-api. Mapea el pre-trámite (field_values vin/plate, actores, comercial).
/// Ante indisponibilidad del canal devuelve grpc_unavailable para que el job reintente.
/// </summary>
public sealed class IctGrpcProcedureDraftClient(IctOrchestration.IctOrchestrationClient client)
    : IProcedureDraftClient
{
    public async Task<CreateDraftResult> CreateDraftAsync(
        ExternalIntegrationMaster master,
        string procedureTypeCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);

        var request = new CreateDraftFromIctRequest
        {
            TenantId = master.TenantId.ToString(),
            ProcedureTypeCode = procedureTypeCode,
            CreatedByUserId = (master.CreatedBy ?? Guid.Empty).ToString(),
            TransitOfficeId = string.Empty,
            Origin = "ict",
            ExternalRef = master.Id.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(master.Vin))
        {
            request.FieldValues.Add(new FieldValue { FieldKey = "vin", ValueText = master.Vin });
        }

        if (!string.IsNullOrWhiteSpace(master.Plate))
        {
            request.FieldValues.Add(new FieldValue { FieldKey = "plate", ValueText = master.Plate });
        }

        foreach (var actor in master.Actors)
        {
            request.Actors.Add(new Actor
            {
                ActorType = actor.ActorType,
                DocumentType = actor.DocumentType,
                DocumentNumber = actor.DocumentNumber,
                FullName = $"{actor.Name} {actor.FirstLastName} {actor.SecondLastName}".Trim(),
                Email = actor.Email,
                Phone = actor.Phone,
            });
        }

        if (master.TransactionType == 3)
        {
            request.Commercial = new CommercialData
            {
                ValorVenta = master.SellingPrice.ToString(CultureInfo.InvariantCulture),
                SellingDate = master.SellingDate,
            };
        }

        try
        {
            var reply = await client.CreateDraftFromIctAsync(request, cancellationToken: ct);
            if (!string.IsNullOrEmpty(reply.ErrorCode))
            {
                return new CreateDraftResult(null, null, null, reply.ErrorCode);
            }

            return new CreateDraftResult(
                Guid.TryParse(reply.ProcedureInstanceId, out var id) ? id : null,
                reply.ReferenceNumber,
                reply.Status,
                null);
        }
        catch (RpcException)
        {
            return new CreateDraftResult(null, null, null, "grpc_unavailable");
        }
    }
}
