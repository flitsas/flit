using System.Globalization;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Domain.Validation;
using Flit.Ict.Grpc.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Flit.Ict.Infrastructure.ExternalClients;

/// <summary>
/// Materializa el borrador en core-api vía gRPC (IctOrchestration.CreateDraftFromIct), reutilizando
/// los casos de uso de core-api. Mapea el pre-trámite (field_values vin/plate + transformaciones y prenda
/// vía IctDraftFieldValuesMapper, actores, comercial).
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
        DraftProcedureType procedureType,
        VehicleConsultationSnapshot? vehicle,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);

        var request = await BuildRequestAsync(master, procedureType, docTypeResolver, vehicle, logger, ct);

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

                // Bug #13304 — el warning viaja en el resultado (antes se descartaba): el job conserva el id
                // (hay id ⇒ éxito) y puede registrar actors_warning/commercial_warning en el master.
                return new CreateDraftResult(
                    id, reply.ReferenceNumber, reply.Status,
                    string.IsNullOrEmpty(reply.ErrorCode) ? null : reply.ErrorCode);
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

    /// <summary>
    /// Arma el request del borrador. Está separado del envío porque es donde viven las decisiones de
    /// ADR-0050: qué tipo se materializa, de dónde sale el organismo de tránsito y si el borrador
    /// lleva datos comerciales. Todas las declara <c>ict.procedure_type_mapping</c>; ninguna se
    /// deduce ya del texto del código ni del número de transacción.
    /// </summary>
    internal static Task<CreateDraftFromIctRequest> BuildRequestAsync(
        ExternalIntegrationMaster master,
        DraftProcedureType procedureType,
        IAttachmentDocTypeResolver docTypeResolver,
        ILogger? log = null,
        CancellationToken ct = default) =>
        BuildRequestAsync(master, procedureType, docTypeResolver, vehicle: null, log, ct);

    /// <summary>
    /// Igual que la sobrecarga sin consulta, y además (Bug #13304) adjunta en el campo 14
    /// <c>precomputed_vehicle</c> la consulta RUNT de la validación ICT para que core-api no re-consulte.
    /// El JSON viaja opaco, sin tocar. <paramref name="vehicle"/> null ⇒ el campo queda ausente.
    /// </summary>
    internal static async Task<CreateDraftFromIctRequest> BuildRequestAsync(
        ExternalIntegrationMaster master,
        DraftProcedureType procedureType,
        IAttachmentDocTypeResolver docTypeResolver,
        VehicleConsultationSnapshot? vehicle,
        ILogger? log,
        CancellationToken ct)
    {
        var request = new CreateDraftFromIctRequest
        {
            TenantId = master.TenantId.ToString(),
            ProcedureTypeCode = procedureType.Code,
            CreatedByUserId = (master.CreatedBy ?? Guid.Empty).ToString(),
            TransitOfficeId = string.Empty,
            Origin = "ict",
            ExternalRef = master.Id.ToString(),
        };

        // Organismo de tránsito (Bug #13109). Primero el código que viajó en la transacción: el SP de negocio
        // ya lo resolvió a transit_office_id (activo + grant del tenant) y se envía tal cual. Si no hay id,
        // hay tipos cuyo organismo lo fija el RUNT (paridad v1 de traspaso): se envía el nombre que capturó
        // el orquestador de la consulta VEHICLE y core-api lo resuelve por nombre. En los demás va vacío y lo
        // asigna el gestor. Quién resuelve por RUNT lo declara el mapeo, no el texto del código (ADR-0050).
        // Con el id viajan el código ya validado y el nombre/city_code de la misma fila del catálogo: core-api
        // siembra con ellos los field_values del OT que exigen sus gates (finalizar, radicar, mandato).
        if (master.TransitOfficeId is { } transitOfficeId && transitOfficeId != Guid.Empty)
        {
            request.TransitOfficeId = transitOfficeId.ToString();
            request.TransitOfficeCode = master.TrafficSecretaryCode ?? string.Empty;
            request.TransitOfficeName = master.TransitOfficeName ?? string.Empty;
            request.TransitOfficeCity = master.TransitOfficeCityCode ?? string.Empty;
        }
        else if (procedureType.ResolvesTransitOfficeFromRunt && !string.IsNullOrWhiteSpace(master.RuntTransitOfficeName))
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

        // Bug #13445 — transformaciones (5/6/7/9, catálogo Tipo Trámite) y prenda viajan como field_values del borrador.
        request.FieldValues.Add(IctDraftFieldValuesMapper.Map(master));

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

        // El valor comercial es una capacidad del tipo (el FUR lo exige en traspaso), no del número 3.
        if (procedureType.RequiresCommercialValue)
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
                if (log is not null)
                {
                    Log.AttachmentSkipped(log, attachment.IdAttachment, master.Id);
                }
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

        if (vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.SnapshotJson))
        {
            request.PrecomputedVehicle = new PrecomputedVehicleConsultation
            {
                SnapshotJson = vehicle.SnapshotJson,
                ConsultedAt = Timestamp.FromDateTimeOffset(vehicle.ConsultedAt),
                Provider = vehicle.Provider ?? string.Empty,
                Kind = vehicle.Kind ?? string.Empty,
                QueriedPlate = vehicle.Plate ?? string.Empty,
                QueriedVin = vehicle.Vin ?? string.Empty,
            };
        }

        return request;
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
    /// Bug #13304 — edita el precio de venta del borrador en core-api (UpdateDraftCommercial). core-api
    /// valida tenant + external_ref y que siga en borrador; su error_code se devuelve tal cual
    /// (<c>not_draft</c>, <c>not_found</c>, <c>invalid_*</c>). Canal caído → <c>grpc_unavailable</c>.
    /// </summary>
    public async Task<(bool Ok, string? Error)> UpdateCommercialAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        Guid externalRef,
        decimal sellingPrice,
        CancellationToken ct = default)
    {
        var request = new UpdateDraftCommercialRequest
        {
            TenantId = tenantId.ToString(),
            ProcedureInstanceId = procedureInstanceId.ToString(),
            ExternalRef = externalRef.ToString(),
            Commercial = new CommercialData
            {
                ValorVenta = sellingPrice.ToString(CultureInfo.InvariantCulture),
            },
        };

        try
        {
            var reply = await client.UpdateDraftCommercialAsync(request, cancellationToken: ct);
            return string.IsNullOrEmpty(reply.ErrorCode) ? (true, null) : (false, reply.ErrorCode);
        }
        catch (RpcException ex)
        {
            Log.GrpcFailed(logger, ex.StatusCode.ToString(), ex.Status.Detail, procedureInstanceId, ex);
            return (false, "grpc_unavailable");
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
