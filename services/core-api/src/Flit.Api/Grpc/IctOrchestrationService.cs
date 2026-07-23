using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Servidor gRPC de orquestación invocado por core-ict (ICT). Adaptador delgado sobre los casos de
/// uso existentes de trámites: crea el borrador y siembra los field_values (vin/plate). NO reimplementa
/// reglas — reutiliza CreateProcedureInstanceHandler + PatchFieldValuesHandler (igual que la
/// importación masiva). El tenant viaja explícito en el mensaje (autorizado por el service-token).
/// TODO(ICT-HU4-ACTORS): mapear actores/comercial/adjuntos (UpsertActors/PatchCommercial/RegisterAttachment).
/// TODO(ICT-HU4-REVERSE): persistir origin/external_ref y empujar cambios de estado de vuelta a core-ict.
/// </summary>
public sealed class IctOrchestrationService(
    CreateProcedureInstanceHandler createHandler,
    PatchFieldValuesHandler patchHandler) : IctOrchestration.IctOrchestrationBase
{
    public override async Task<DraftReply> CreateDraftFromIct(
        CreateDraftFromIctRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            return new DraftReply { ErrorCode = "invalid_tenant" };
        }

        _ = Guid.TryParse(request.CreatedByUserId, out var createdBy);
        Guid? transitOfficeId = Guid.TryParse(request.TransitOfficeId, out var office) ? office : null;

        var createRequest = new CreateProcedureInstanceRequest(
            TenantId: tenantId,
            ProcedureTypeId: null,
            CreatedByUserId: createdBy,
            TransitOfficeId: transitOfficeId,
            Modalidad: null,
            ProcedureTypeCode: request.ProcedureTypeCode);

        var (summary, error) = await createHandler.HandleAsync(createRequest, context.CancellationToken);
        if (error is not null || summary is null)
        {
            return new DraftReply { ErrorCode = error ?? "create_failed" };
        }

        var reply = new DraftReply
        {
            ProcedureInstanceId = summary.Id.ToString(),
            ReferenceNumber = summary.ReferenceNumber,
            Status = summary.Status,
        };

        if (request.FieldValues.Count > 0)
        {
            var items = request.FieldValues
                .Select(f => new FieldValueInput(null, f.FieldKey, f.ValueText, null))
                .ToList();
            var (_, patchError) = await patchHandler.HandleAsync(
                summary.Id,
                tenantId,
                new Flit.Tramites.Application.UseCases.ProcedureInstances.PatchFieldValuesRequest(items),
                context.CancellationToken);
            if (patchError is not null)
            {
                reply.ErrorCode = "seed_warning:" + patchError;
            }
        }

        return reply;
    }
}
