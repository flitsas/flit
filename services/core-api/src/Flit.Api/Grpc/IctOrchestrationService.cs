using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

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
    PatchFieldValuesHandler patchHandler,
    FlitDbContext db) : IctOrchestration.IctOrchestrationBase
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

        // El creador de un trámite originado en ICT no es un usuario de plataforma (los clientes ICT
        // viven en ict.integration_clients, no en identity.users). procedure_instances.created_by_user_id
        // es FK NOT NULL a identity.users, así que se resuelve (get-or-create) un usuario de servicio ICT
        // por tenant. Ignora request.CreatedByUserId salvo que sea un usuario real ya existente.
        var createdBy = await ResolveIctCreatorAsync(request.CreatedByUserId, tenantId, context.CancellationToken);
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

    /// <summary>
    /// Resuelve el usuario creador para un trámite ICT. Si <paramref name="requestedUserId"/> es un
    /// usuario real existente, lo usa; si no, obtiene-o-crea (idempotente) un usuario de servicio ICT
    /// del tenant (<c>ict-integration+{tenant}@flit.local</c>), sin credenciales de plataforma.
    /// TODO(ICT-SERVICE-USER): aprovisionar este usuario en el alta del cliente de integración y
    /// asignarle un rol de solo-lectura, en vez de crearlo perezosamente aquí.
    /// </summary>
    private async Task<Guid> ResolveIctCreatorAsync(string requestedUserId, Guid tenantId, CancellationToken ct)
    {
        if (Guid.TryParse(requestedUserId, out var requested) && requested != Guid.Empty)
        {
            var exists = await db.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM identity.users WHERE id = {requested} AND deleted_at IS NULL")
                .AnyAsync(ct);
            if (exists)
            {
                return requested;
            }
        }

        var email = $"ict-integration+{tenantId}@flit.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.users (id, email, display_name, status, created_at, home_tenant_id)
            VALUES (uuidv7(), {email}, 'Integración ICT', 'active', now(), {tenantId})
            ON CONFLICT (email) DO NOTHING
            """, ct);

        return await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM identity.users WHERE email = {email}")
            .FirstAsync(ct);
    }
}
