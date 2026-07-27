using System.Globalization;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Tramites.Estados;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Grpc;

/// <summary>
/// Servidor gRPC de orquestación invocado por core-ict (ICT). Adaptador delgado sobre los casos de
/// uso existentes de trámites: crea el borrador y siembra los field_values (vin/plate). NO reimplementa
/// reglas — reutiliza CreateProcedureInstanceHandler + PatchFieldValuesHandler + PutActorsHandler +
/// PutCommercialHandler + RegisterIntegrationAttachmentHandler (igual que la importación masiva y el
/// wizard). El tenant viaja explícito en el mensaje (autorizado por el service-token).
/// TODO(ICT-HU4-REVERSE): persistir origin/external_ref y empujar cambios de estado de vuelta a core-ict.
/// </summary>
public sealed class IctOrchestrationService(
    CreateProcedureInstanceHandler createHandler,
    PatchFieldValuesHandler patchHandler,
    PutActorsHandler actorsHandler,
    PutCommercialHandler commercialHandler,
    RegisterIntegrationAttachmentHandler attachmentsHandler,
    TransitionProcedureInstanceHandler transitionHandler,
    RunConsultationHandler runConsultationHandler,
    ITransitOfficeResolver transitOfficeResolver,
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

        // Idempotencia de la materialización: si este pre-trámite (external_ref) YA se materializó —p. ej.
        // el Job 4 se reintenta tras caerse ANTES de grabar la correlación en el master—, se devuelve el
        // MISMO borrador sin crear otro. El índice único parcial (tenant_id, external_ref) es la red final.
        var externalRef = string.IsNullOrWhiteSpace(request.ExternalRef) ? null : request.ExternalRef.Trim();
        if (externalRef is not null)
        {
            var existing = await db.Set<ProcedureInstance>()
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.ExternalRef == externalRef && p.DeletedAt == null)
                .Select(p => new { p.Id, p.ReferenceNumber, p.Status })
                .FirstOrDefaultAsync(context.CancellationToken);
            if (existing is not null)
            {
                return new DraftReply
                {
                    ProcedureInstanceId = existing.Id.ToString(),
                    ReferenceNumber = existing.ReferenceNumber,
                    Status = existing.Status,
                };
            }
        }

        // El creador de un trámite originado en ICT no es un usuario de plataforma (los clientes ICT
        // viven en ict.integration_clients, no en identity.users). procedure_instances.created_by_user_id
        // es FK NOT NULL a identity.users, así que se resuelve (get-or-create) un usuario de servicio ICT
        // por tenant. Ignora request.CreatedByUserId salvo que sea un usuario real ya existente.
        var createdBy = await ResolveIctCreatorAsync(request.CreatedByUserId, tenantId, context.CancellationToken);

        // Organismo de tránsito del borrador. Preferencia: un transit_office_id explícito; si no vino
        // (caso ICT: el cliente NO lo manda) pero sí el NOMBRE del organismo del RUNT (traspaso), se
        // resuelve al OT HABILITADO del tenant por nombre — el mismo resolver (grants + catálogo) que usa
        // el preflight de traspaso. Si el nombre RUNT no casa con un OT habilitado, queda null y el gestor
        // asigna el OT: no se inventa uno. Paridad con v1, donde el traspaso derivaba la secretaría del RUNT.
        Guid? transitOfficeId = Guid.TryParse(request.TransitOfficeId, out var office) ? office : null;
        if (transitOfficeId is null && !string.IsNullOrWhiteSpace(request.TransitOfficeName))
        {
            var resolvedOffice = await transitOfficeResolver.ResolveEnabledByNameAsync(
                tenantId, request.TransitOfficeName.Trim(), context.CancellationToken);
            transitOfficeId = resolvedOffice?.Id;
        }

        var createRequest = new CreateProcedureInstanceRequest(
            TenantId: tenantId,
            ProcedureTypeId: null,
            CreatedByUserId: createdBy,
            TransitOfficeId: transitOfficeId,
            Modalidad: null,
            ProcedureTypeCode: request.ProcedureTypeCode,
            Origin: string.IsNullOrWhiteSpace(request.Origin) ? "ict" : request.Origin.Trim(),
            ExternalRef: externalRef);

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

        // field_values del pre-trámite: vin/plate (los envía core-ict) + los datos del LOCATARIO
        // cuando el trámite NO es traspaso (matrícula leasing tipo 2 / cambio de locatario tipo 8). El
        // locatario no es una parte comprador/vendedor en esos trámites, así que se preserva como
        // atributo del trámite (valores "loose", FormFieldId=null). En traspaso (tipos 3/4) el locatario
        // se materializa como comprador (ver MapActors), no como atributo.
        var fieldItems = request.FieldValues
            .Select(f => new FieldValueInput(null, f.FieldKey, f.ValueText, null))
            .ToList();
        fieldItems.AddRange(MapLesseeFieldValues(request.Actors, request.ProcedureTypeCode));

        // Fecha de venta del traspaso (contrato v1): v2 NO la modela en el comercial
        // (procedure_instance_commercial no tiene columna de fecha) ni hay form_field para ella, así que
        // se preserva como valor "loose" para no perder el dato. El valor de venta / causal sí van al
        // comercial (más abajo). TODO(ICT-SELLING-DATE-UI): el front aún no renderiza selling_date.
        if (request.Commercial is { SellingDate: var sellingDate } && !string.IsNullOrWhiteSpace(sellingDate))
        {
            fieldItems.Add(new FieldValueInput(null, "selling_date", sellingDate.Trim(), null));
        }

        // Documento del TITULAR (vendedor en traspaso / propietario). El registro manual lo captura como
        // owner_document_* y ADEMÁS la consulta RUNT por placa lo EXIGE (VerifikConsultationProvider lee
        // owner_document_type/number). Se deriva del actor titular que envía ICT (seller/owner) para que
        // el borrador quede con el mismo dato que el manual y la hidratación del vehículo (más abajo) pueda
        // consultar por placa. En matrícula (consulta por VIN) es igualmente el propietario del vehículo.
        var titular = request.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType?.Trim(), "seller", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.ActorType?.Trim(), "owner", StringComparison.OrdinalIgnoreCase));
        if (titular is not null && !string.IsNullOrWhiteSpace(titular.DocumentNumber))
        {
            fieldItems.Add(new FieldValueInput(null, "owner_document_type",
                string.IsNullOrWhiteSpace(titular.DocumentType) ? "CC" : titular.DocumentType.Trim().ToUpperInvariant(), null));
            fieldItems.Add(new FieldValueInput(null, "owner_document_number", titular.DocumentNumber.Trim(), null));
        }

        // Atributos "manuales" del traspaso que el frontend le pediría al gestor y que el contrato ICT v1
        // NO trae (el master solo modela selling_price/vin/selling_date): se siembran con el default del
        // caso estándar (no leasing, sin cambio de carrocería) para que el wizard no vuelva a solicitarlos.
        // El gestor puede cambiarlos. accion_prenda se OMITE a propósito: es una decisión de negocio que
        // depende de si el vehículo tiene gravamen (levantar/mantener) y no es derivable del payload ICT.
        // TODO(ICT-MANUAL-ATTRS): derivar es_leasing/cambio_carroceria del tenant/transformaciones del
        // master cuando ICT los capture (external_integration_master_transformation_type).
        var esTraspasoDraft = request.ProcedureTypeCode?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
        if (esTraspasoDraft)
        {
            fieldItems.Add(new FieldValueInput(null, "es_leasing", "false", null));
            fieldItems.Add(new FieldValueInput(null, "cambio_carroceria", "false", null));
        }

        if (fieldItems.Count > 0)
        {
            var (_, patchError) = await patchHandler.HandleAsync(
                summary.Id,
                tenantId,
                new Flit.Tramites.Application.UseCases.ProcedureInstances.PatchFieldValuesRequest(fieldItems),
                context.CancellationToken);
            if (patchError is not null)
            {
                AppendWarning(reply, "seed_warning:" + patchError);
            }
        }

        // Actores del pre-trámite (vendedor/comprador + su representante legal). Se reutiliza
        // PutActorsHandler, el único escritor de partes. Fallo NO fatal: el borrador ya existe y el
        // gestor puede completarlo; se reporta como warning para no perder la trazabilidad.
        var actorInputs = MapActors(request.Actors, request.ProcedureTypeCode);
        if (actorInputs.Count > 0)
        {
            var (_, actorsError) = await actorsHandler.HandleAsync(
                summary.Id, tenantId, new PutActorsRequest(actorInputs), context.CancellationToken);
            if (actorsError is not null)
            {
                AppendWarning(reply, "actors_warning:" + actorsError);
            }
        }

        // Datos comerciales del traspaso (valor de venta / causal / método de pago). core-ict los envía en
        // request.Commercial SOLO para traspaso (tipo 3); en el resto de trámites llega null. Se reutiliza
        // PutCommercialHandler (el MISMO escritor que el paso 5 del wizard) para que el valor de venta
        // aparezca en el borrador y el gestor lo vea. El handler exige una causal del catálogo cerrado; el
        // contrato v1 no la trae, así que se usa COMPRAVENTA por defecto (la causal dominante del traspaso
        // y la primera del selector del front) y el gestor puede cambiarla. Fallo NO fatal: el borrador ya
        // existe y se reporta como warning acumulado.
        if (request.Commercial is { } commercial
            && decimal.TryParse(commercial.ValorVenta, NumberStyles.Any, CultureInfo.InvariantCulture, out var valorVenta)
            && valorVenta > 0)
        {
            var causal = string.IsNullOrWhiteSpace(commercial.Causal)
                ? "COMPRAVENTA"
                : commercial.Causal.Trim().ToUpperInvariant();
            var commercialDto = new CommercialDto(
                ValorVenta: valorVenta,
                Causal: causal,
                TasaImpuesto: null,
                Derechos: null,
                MetodoPago: string.IsNullOrWhiteSpace(commercial.MetodoPago) ? null : commercial.MetodoPago.Trim());
            var (_, commercialError) = await commercialHandler.HandleAsync(
                summary.Id, tenantId, commercialDto, context.CancellationToken);
            if (commercialError is not null)
            {
                AppendWarning(reply, "commercial_warning:" + commercialError);
            }
        }

        // Adjuntos del pre-trámite: se registran por REFERENCIA (el binario ya está en el File Manager
        // corporativo; core-ict envía la metadata + storage_path + el DocTipo YA resuelto por su tabla de
        // asociación). No se re-suben bytes; se registra la misma referencia S3. Fallo NO fatal: el
        // borrador ya existe y el gestor puede completarlo; se reporta como warning acumulado.
        if (request.Attachments.Count > 0)
        {
            // Todos los adjuntos en UNA unidad de trabajo (un solo SaveChanges): registrarlos uno por uno
            // reventaría por el token de concurrencia de la instancia al reincidir el AutoMark. Ver
            // RegisterIntegrationAttachmentHandler.HandleBatchAsync.
            var attachmentInputs = request.Attachments
                .Select(att => new RegisterAttachmentInput(
                    Tipo: att.DocumentType,
                    Filename: att.Filename,
                    Mimetype: att.MimeType,
                    SizeBytes: att.SizeBytes,
                    Sha256: att.Sha256,
                    StoragePath: att.StoragePath))
                .ToList();
            var (_, attachmentWarnings) = await attachmentsHandler.HandleBatchAsync(
                summary.Id, tenantId, attachmentInputs, createdBy, context.CancellationToken);
            if (attachmentWarnings.Count > 0)
            {
                AppendWarning(reply, "attachments_warning:" + string.Join(",", attachmentWarnings));
            }
        }

        // Hidratación de los datos del VEHÍCULO desde el RUNT — PARIDAD con el registro manual del
        // frontend. Reutiliza el MISMO RunConsultationHandler del wizard (template RUNT_VEHICLE →
        // provider verifik) que upserta vehicle_*/soat_*/vin como field_values con Source="consultation".
        // Sin esto el borrador de ICT solo tendría plate/vin y el gestor tendría que pulsar "Consultar
        // RUNT" y reingresar los ~19 atributos que el manual llena solo. Se ejecuta cuando hay placa o VIN
        // (traspaso consulta por placa+owner_document_*; matrícula por VIN). Best-effort: cualquier fallo
        // del proveedor NO tumba la materialización (el borrador ya existe) — se degrada a warning y el
        // gestor puede consultar manualmente. Mismo contrato que el flujo manual, cero reglas nuevas.
        var tieneVehiculo = request.FieldValues.Any(f =>
            (string.Equals(f.FieldKey, "plate", StringComparison.OrdinalIgnoreCase)
             || string.Equals(f.FieldKey, "vin", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(f.ValueText));
        if (tieneVehiculo)
        {
            try
            {
                var (_, hydrateError) = await runConsultationHandler.HandleAsync(
                    summary.Id, tenantId, "RUNT_VEHICLE", forceRefresh: false, context.CancellationToken);
                if (hydrateError is not null)
                {
                    AppendWarning(reply, "hydrate_warning:" + hydrateError);
                }
            }
            catch (Exception) when (!context.CancellationToken.IsCancellationRequested)
            {
                AppendWarning(reply, "hydrate_warning:exception");
            }
        }

        return reply;
    }

    /// <summary>
    /// Acumula un warning NO fatal en <c>reply.ErrorCode</c> sin pisar los previos (separados por ';').
    /// El borrador YA existe: el cliente ICT debe conservar el ProcedureInstanceId y tratar esto como
    /// aviso, no como fallo (ver IctGrpcProcedureDraftClient.CreateDraftAsync).
    /// </summary>
    private static void AppendWarning(DraftReply reply, string warning) =>
        reply.ErrorCode = string.IsNullOrEmpty(reply.ErrorCode) ? warning : reply.ErrorCode + ";" + warning;

    /// <summary>
    /// Traduce los actores del contrato ICT al vocabulario de partes de trámites.
    /// OJO con la semántica de v1: <c>seller</c> NO siempre es un vendedor — es el slot del TITULAR /
    /// firmante principal. En TRASPASO (3,4) es el titular saliente (→ <c>vendedor</c>, entidad OWNER);
    /// en MATRÍCULA (1,2) y otros trámites (5-16) es el propietario/adquirente (→ <c>comprador</c>,
    /// entidad BUYER), que es además el único rol que la matriz de tipología admite en matrícula.
    /// v1: transactionPayloadBuilderService.ts:77 (isTransferTransaction) y :201-214 (bloque vehicleOwner*),
    /// donde el email va a emailSeller solo si es traspaso y a emailOwner en el resto.
    /// El <c>buyer</c> solo aplica a traspaso; v1 lo descarta explícitamente fuera de traspaso (:252-258).
    /// LOCATARIO (<c>lessee</c>, tipos 2/4/8): en TRASPASO (el caso vivo es el unilateral tipo 4) el
    /// locatario ES el adquirente, así que toma el slot <c>comprador</c> con PRECEDENCIA sobre un buyer
    /// explícito (v1 sobreescribe vehicleBuyer con el lessee: transactionPayloadBuilderService.ts:285-295).
    /// Fuera de traspaso (matrícula leasing tipo 2 / cambio de locatario tipo 8) el locatario NO es una
    /// parte comprador/vendedor: se preserva como atributo del trámite en <see cref="MapLesseeFieldValues"/>
    /// (v1 lo guardaba en columnas *_le del master).
    /// </summary>
    private static List<ActorInput> MapActors(
        IEnumerable<Flit.Ict.Grpc.Contracts.Actor> actors,
        string? procedureTypeCode)
    {
        var esTraspaso = procedureTypeCode?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
        var list = actors as IReadOnlyList<Flit.Ict.Grpc.Contracts.Actor> ?? actors.ToList();
        var result = new List<ActorInput>();

        // Titular (seller/owner): vendedor en traspaso, comprador en el resto.
        foreach (var a in list)
        {
            var type = a.ActorType?.Trim().ToLowerInvariant();
            if (type is "seller" or "owner")
            {
                TryAddActor(result, a, esTraspaso ? "vendedor" : "comprador");
            }
        }

        // Slot comprador en traspaso: el locatario toma precedencia sobre el buyer explícito (v1 lo
        // sobreescribe); si no hay locatario, el buyer. Fuera de traspaso, ni buyer ni lessee son
        // comprador (el titular ya ocupa ese rol; el buyer se descarta, el lessee va a atributos).
        if (esTraspaso)
        {
            var compradorSource =
                list.FirstOrDefault(a => string.Equals(a.ActorType?.Trim(), "lessee", StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(a => string.Equals(a.ActorType?.Trim(), "buyer", StringComparison.OrdinalIgnoreCase));
            if (compradorSource is not null)
            {
                TryAddActor(result, compradorSource, "comprador");
            }
        }

        return result;
    }

    /// <summary>
    /// Agrega un actor con el rol dado si trae los mínimos que exige PutActorsHandler (email con formato
    /// válido y documento) y ese rol aún no está tomado (un actor por rol; el primero gana). El fallo de
    /// un actor no tumba el lote: simplemente se omite ese actor.
    /// </summary>
    private static void TryAddActor(List<ActorInput> result, Flit.Ict.Grpc.Contracts.Actor a, string rol)
    {
        if (result.Any(x => string.Equals(x.Rol, rol, StringComparison.Ordinal)))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(a.Email) || string.IsNullOrWhiteSpace(a.DocumentNumber))
        {
            return;
        }

        var personType = string.Equals(a.DocumentType?.Trim(), "NIT", StringComparison.OrdinalIgnoreCase)
            ? "juridical"
            : "natural";

        result.Add(new ActorInput(
            Rol: rol,
            TipoDocumento: a.DocumentType?.Trim().ToUpperInvariant() ?? string.Empty,
            NumeroDocumento: a.DocumentNumber.Trim(),
            NombreCompleto: a.FullName?.Trim() ?? string.Empty,
            Email: a.Email.Trim(),
            Telefono: string.IsNullOrWhiteSpace(a.Phone) ? null : a.Phone.Trim(),
            Ciudad: MetaString(a.Metadata, "city"),
            Direccion: MetaString(a.Metadata, "address"),
            PersonType: personType,
            EsRepresentanteLegal: false,
            RepresentanteLegal: MapRepresentanteLegal(a.Metadata),
            Mandante: MapMandante(a.Metadata)));
    }

    /// <summary>
    /// Datos del LOCATARIO (lessee) como atributos "loose" del trámite, para los trámites que NO son
    /// traspaso (matrícula leasing tipo 2 / cambio de locatario tipo 8). El locatario ahí no es una parte
    /// comprador/vendedor (ParteRol no lo tiene y la matriz de tipología lo rechazaría), pero su
    /// información no debe perderse: v1 la guardaba en columnas <c>vehicle_owner_*_le</c> del master; en
    /// v2 se persiste como field_values <c>lessee_*</c> (FormFieldId=null, sin catálogo de campo — el
    /// PatchFieldValuesHandler los acepta como valores "loose"). En traspaso devuelve vacío: allí el
    /// locatario se materializa como comprador (ver <see cref="MapActors"/>).
    /// TODO(ICT-LESSEE-UI): el frontend aún no renderiza las claves lessee_*; hoy solo preservan el dato.
    /// </summary>
    private static List<FieldValueInput> MapLesseeFieldValues(
        IEnumerable<Flit.Ict.Grpc.Contracts.Actor> actors,
        string? procedureTypeCode)
    {
        var esTraspaso = procedureTypeCode?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
        if (esTraspaso)
        {
            return [];
        }

        var lessee = actors.FirstOrDefault(a =>
            string.Equals(a.ActorType?.Trim(), "lessee", StringComparison.OrdinalIgnoreCase));
        if (lessee is null)
        {
            return [];
        }

        var items = new List<FieldValueInput>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(new FieldValueInput(null, key, value.Trim(), null));
            }
        }

        Add("lessee_document_type", lessee.DocumentType);
        Add("lessee_document_number", lessee.DocumentNumber);
        Add("lessee_full_name", lessee.FullName);
        Add("lessee_email", lessee.Email);
        Add("lessee_phone", lessee.Phone);
        Add("lessee_city", MetaString(lessee.Metadata, "city"));
        Add("lessee_address", MetaString(lessee.Metadata, "address"));
        return items;
    }

    /// <summary>Representante legal embebido en el metadata del actor (contrato v1 legal_representative).</summary>
    private static ActorRepresentanteLegal? MapRepresentanteLegal(Struct? metadata)
    {
        if (metadata is null
            || !metadata.Fields.TryGetValue("legal_representative", out var value)
            || value.KindCase != Value.KindOneofCase.StructValue)
        {
            return null;
        }

        var rl = value.StructValue;
        return new ActorRepresentanteLegal(
            MetaString(rl, "document_type"),
            MetaString(rl, "document_number"),
            MetaString(rl, "full_name"),
            MetaString(rl, "email"),
            MetaString(rl, "phone"));
    }

    /// <summary>Mandante embebido en el metadata del actor (contrato v1 principal_mandante).</summary>
    private static ActorMandante? MapMandante(Struct? metadata)
    {
        if (metadata is null
            || !metadata.Fields.TryGetValue("principal_mandante", out var value)
            || value.KindCase != Value.KindOneofCase.StructValue)
        {
            return null;
        }

        var m = value.StructValue;
        return new ActorMandante(
            MetaString(m, "document_type"),
            MetaString(m, "document_number"),
            MetaString(m, "full_name"),
            MetaString(m, "email"));
    }

    private static string? MetaString(Struct? metadata, string key) =>
        metadata is not null
        && metadata.Fields.TryGetValue(key, out var value)
        && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;

    /// <summary>
    /// Anula un borrador (servicio v1 abortProcess). Reutiliza el ciclo de vida de trámites
    /// (<see cref="TransitionProcedureInstanceHandler"/> → estado <c>anulado</c>): valida la máquina de
    /// estados (borrador/rechazado → anulado), exige motivo, y escribe historial + evento en una unidad
    /// de trabajo. El <c>changed_by</c> es el usuario de servicio ICT del tenant.
    /// </summary>
    public override async Task<DraftReply> AbortDraft(AbortDraftRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            return new DraftReply { ErrorCode = "invalid_tenant" };
        }

        if (!Guid.TryParse(request.ProcedureInstanceId, out var instanceId))
        {
            return new DraftReply { ErrorCode = "invalid_instance" };
        }

        // El motivo es obligatorio para anular (motivo_requerido); si el cliente no lo envía, se usa
        // una nota por defecto trazable al origen ICT.
        var reason = string.IsNullOrWhiteSpace(request.Observation)
            ? "Anulado por integración ICT"
            : request.Observation;
        var changedBy = await ResolveIctCreatorAsync(string.Empty, tenantId, context.CancellationToken);

        var (result, errorCode, _) = await transitionHandler.HandleAsync(
            instanceId, tenantId, TramiteEstado.Anulado, reason, changedBy, context.CancellationToken);
        if (errorCode is not null || result is null)
        {
            return new DraftReply { ErrorCode = errorCode ?? "abort_failed" };
        }

        return new DraftReply
        {
            ProcedureInstanceId = result.Id.ToString(),
            ReferenceNumber = result.ReferenceNumber,
            Status = result.Status,
        };
    }

    /// <summary>
    /// Pausa o reanuda un borrador (servicio v1 pauseDraftProcess). FLIT 2.0 no tenía pausa: se persiste
    /// en las columnas aditivas <c>is_paused</c>/<c>paused_observation</c> de procedure_instances. Solo
    /// aplica en estado <c>borrador</c> (como v1). TODO(ICT-PAUSE-UI): reflejar la pausa en el dashboard.
    /// </summary>
    public override async Task<DraftReply> PauseDraft(PauseDraftRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            return new DraftReply { ErrorCode = "invalid_tenant" };
        }

        if (!Guid.TryParse(request.ProcedureInstanceId, out var instanceId))
        {
            return new DraftReply { ErrorCode = "invalid_instance" };
        }

        var status = await db.Database
            .SqlQuery<string>($"""
                SELECT status AS "Value" FROM tramites.procedure_instances
                WHERE id = {instanceId} AND tenant_id = {tenantId} AND deleted_at IS NULL
                """)
            .FirstOrDefaultAsync(context.CancellationToken);
        if (status is null)
        {
            return new DraftReply { ErrorCode = "not_found" };
        }

        if (!string.Equals(status, TramiteEstado.Borrador, StringComparison.Ordinal))
        {
            // v1: "El trámite no se encuentra en estado de borrador".
            return new DraftReply { ErrorCode = "not_in_draft" };
        }

        var changedBy = await ResolveIctCreatorAsync(string.Empty, tenantId, context.CancellationToken);
        var observation = request.Paused ? request.Observation : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE tramites.procedure_instances
            SET is_paused = {request.Paused},
                paused_observation = {observation},
                updated_at = now(),
                updated_by = {changedBy}
            WHERE id = {instanceId} AND tenant_id = {tenantId}
            """, context.CancellationToken);

        return new DraftReply { ProcedureInstanceId = instanceId.ToString(), Status = status };
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
