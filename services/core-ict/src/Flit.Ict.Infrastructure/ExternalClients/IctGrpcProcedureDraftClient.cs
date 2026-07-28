using System.Globalization;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Grpc.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Materializa el borrador en core-api vía gRPC (IctOrchestration.CreateDraftFromIct), reutilizando
/// los casos de uso de core-api. Mapea el pre-trámite (field_values vin/plate, actores, comercial).
/// Ante indisponibilidad del canal devuelve grpc_unavailable para que el job reintente.
/// </summary>
public sealed partial class IctGrpcProcedureDraftClient(
    IctOrchestration.IctOrchestrationClient client,
    IAttachmentDocTypeResolver docTypeResolver,
    ILogger<IctGrpcProcedureDraftClient> logger)
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

        // Traspaso: el organismo de tránsito lo fija el RUNT (paridad v1), no el código del cliente. Se
        // envía el nombre que capturó el orquestador de la consulta VEHICLE; core-api lo resuelve al
        // transit_office_id del catálogo (grants del tenant). Matrícula (1/2) mantiene el comportamiento
        // actual (transit_office_id vacío → el gestor asigna el OT).
        // TODO(ICT-OT-MATRICULA): en matrícula, resolver el OT por el traffic_secretary_code del cliente.
        var esTraspaso = procedureTypeCode?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
        if (esTraspaso && !string.IsNullOrWhiteSpace(master.RuntTransitOfficeName))
        {
            request.TransitOfficeName = master.RuntTransitOfficeName;
        }

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
                Metadata = BuildActorMetadata(actor),
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

        // Adjuntos por REFERENCIA: el binario ya está en el File Manager corporativo (mismo backend que
        // core-api); se envía solo la metadata + storage_path para que core-api registre el mismo objeto.
        // core-ict resuelve el DocTipo (dueño de la tabla de asociación); core-api lo registra tal cual.
        foreach (var attachment in master.Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.StoragePath) || string.IsNullOrWhiteSpace(attachment.Sha256))
            {
                // Sin objeto durable ni hash no hay nada que referenciar (core-api los exige). Se registra
                // el descarte para no perderlo en silencio; la fila ICT persiste como fuente de verdad.
                Log.AttachmentSkipped(logger, attachment.IdAttachment, master.Id);
                continue;
            }

            var docType = await docTypeResolver.ResolveDocTypeAsync(master.TransactionType, attachment.IdAttachment, ct);
            request.Attachments.Add(new AttachmentRef
            {
                DocumentType = docType,
                Filename = attachment.Filename ?? string.Empty,
                MimeType = string.IsNullOrWhiteSpace(attachment.MimeType) ? "application/pdf" : attachment.MimeType,
                SizeBytes = attachment.SizeBytes,
                Sha256 = attachment.Sha256,
                StoragePath = attachment.StoragePath,
            });
        }

        try
        {
            var reply = await client.CreateDraftFromIctAsync(request, cancellationToken: ct);

            // El servidor devuelve el ProcedureInstanceId cuando el borrador SE CREÓ, incluso si adjunta un
            // ErrorCode: los prefijos seed_warning/actors_warning/attachments_warning son NO FATALES (el
            // borrador ya existe y el gestor puede completarlo). Solo es fallo real cuando NO hay
            // ProcedureInstanceId (invalid_tenant/create_failed/not_published...). Descartar el id ante un
            // warning dejaría el borrador HUÉRFANO en core-api y marcaría el master con novedades falsas.
            if (Guid.TryParse(reply.ProcedureInstanceId, out var id) && id != Guid.Empty)
            {
                if (!string.IsNullOrEmpty(reply.ErrorCode))
                {
                    Log.MaterializedWithWarning(logger, reply.ErrorCode, master.Id);
                }

                return new CreateDraftResult(id, reply.ReferenceNumber, reply.Status, null);
            }

            return new CreateDraftResult(
                null, null, null,
                string.IsNullOrEmpty(reply.ErrorCode) ? "create_failed" : reply.ErrorCode);
        }
        catch (RpcException ex)
        {
            Log.GrpcFailed(logger, ex.StatusCode.ToString(), ex.Status.Detail, master.Id, ex);
            return new CreateDraftResult(null, null, null, "grpc_unavailable");
        }
    }

    public async Task<DraftActionResult> PauseDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        bool paused,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default)
    {
        var request = new PauseDraftRequest
        {
            TenantId = tenantId.ToString(),
            ProcedureInstanceId = procedureInstanceId.ToString(),
            Paused = paused,
            Observation = observation ?? string.Empty,
            ActorUser = actorUser ?? string.Empty,
            ActorMail = actorMail ?? string.Empty,
            ActorCompany = actorCompany ?? string.Empty,
        };

        try
        {
            var reply = await client.PauseDraftAsync(request, cancellationToken: ct);
            return new DraftActionResult(reply.Status, string.IsNullOrEmpty(reply.ErrorCode) ? null : reply.ErrorCode);
        }
        catch (RpcException ex)
        {
            Log.GrpcFailed(logger, ex.StatusCode.ToString(), ex.Status.Detail, procedureInstanceId, ex);
            return new DraftActionResult(null, "grpc_unavailable");
        }
    }

    public async Task<DraftActionResult> AbortDraftAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string observation,
        string actorUser,
        string actorMail,
        string actorCompany,
        CancellationToken ct = default)
    {
        var request = new AbortDraftRequest
        {
            TenantId = tenantId.ToString(),
            ProcedureInstanceId = procedureInstanceId.ToString(),
            Observation = observation ?? string.Empty,
            ActorUser = actorUser ?? string.Empty,
            ActorMail = actorMail ?? string.Empty,
            ActorCompany = actorCompany ?? string.Empty,
        };

        try
        {
            var reply = await client.AbortDraftAsync(request, cancellationToken: ct);
            return new DraftActionResult(reply.Status, string.IsNullOrEmpty(reply.ErrorCode) ? null : reply.ErrorCode);
        }
        catch (RpcException ex)
        {
            Log.GrpcFailed(logger, ex.StatusCode.ToString(), ex.Status.Detail, procedureInstanceId, ex);
            return new DraftActionResult(null, "grpc_unavailable");
        }
    }

    /// <summary>
    /// Empaqueta en <c>Actor.metadata</c> los datos del actor que no caben en los escalares del proto:
    /// ciudad/dirección y, sobre todo, el REPRESENTANTE LEGAL y el MANDANTE del contrato v1. core-api
    /// los persiste embebidos en <c>procedure_instance_actors.metadata</c> (sin DDL).
    /// </summary>
    private static Struct BuildActorMetadata(ExternalIntegrationActor actor)
    {
        var metadata = new Struct();
        Put(metadata, "city", actor.City);
        Put(metadata, "address", actor.Address);

        var rl = new Struct();
        Put(rl, "document_type", actor.LegalRepresentativeDocumentType);
        Put(rl, "document_number", actor.LegalRepresentativeDocumentNumber);
        Put(rl, "full_name", Join(actor.LegalRepresentativeName, actor.LegalRepresentativeFirstLastName, actor.LegalRepresentativeSecondLastName));
        Put(rl, "email", actor.LegalRepresentativeEmail);
        Put(rl, "phone", actor.LegalRepresentativePhone);
        Put(rl, "city", actor.LegalRepresentativeCity);
        Put(rl, "state", actor.LegalRepresentativeState);
        Put(rl, "address", actor.LegalRepresentativeAddress);
        if (rl.Fields.Count > 0)
        {
            metadata.Fields["legal_representative"] = Value.ForStruct(rl);
        }

        var mandante = new Struct();
        Put(mandante, "document_type", actor.PrincipalMandanteDocumentType);
        Put(mandante, "document_number", actor.PrincipalMandanteDocumentNumber);
        Put(mandante, "full_name", Join(actor.PrincipalMandanteName, actor.PrincipalMandanteFirstLastName, actor.PrincipalMandanteSecondLastName));
        Put(mandante, "email", actor.PrincipalMandanteEmail);
        if (mandante.Fields.Count > 0)
        {
            metadata.Fields["principal_mandante"] = Value.ForStruct(mandante);
        }

        return metadata;
    }

    private static void Put(Struct target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Fields[key] = Value.ForString(value.Trim());
        }
    }

    private static string? Join(params string?[] parts)
    {
        var joined = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "ICT gRPC falló (status={GrpcStatus}, detail={Detail}) para {EntityId}; se reintentará.")]
        public static partial void GrpcFailed(ILogger logger, string grpcStatus, string detail, Guid entityId, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Borrador ICT materializado CON warning ({Warning}) para master {MasterId}; el borrador existe y se conserva la correlación.")]
        public static partial void MaterializedWithWarning(ILogger logger, string warning, Guid masterId);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Adjunto ICT id_attachment={IdAttachment} del master {MasterId} sin storage_path/sha256; se omite de la materialización (fila ICT persiste).")]
        public static partial void AttachmentSkipped(ILogger logger, int idAttachment, Guid masterId);
    }
}
