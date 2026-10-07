using System.Globalization;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    RunPreflightHandler preflightHandler,
    EnsureIdentityAndNotifyHandler identityNotifier,
    RepresentanteLegalDesdeDirectorio representanteDirectorio,
    ITransitOfficeResolver transitOfficeResolver,
    FlitDbContext db,
    IConfiguration configuration,
    ILogger<IctOrchestrationService> logger) : IctOrchestration.IctOrchestrationBase
{
    /// <summary>Bug #13304 — vigencia por defecto de la consulta RUNT de la validación ICT.</summary>
    internal const int DefaultVehicleConsultationMaxAgeHours = 24;

    /// <summary>Bug #13304 (L-1) — margen de reloj tolerado para un <c>consulted_at</c> en el futuro.</summary>
    internal static readonly TimeSpan MaxConsultedAtClockSkew = TimeSpan.FromMinutes(5);

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
                .Select(p => new { p.Id, p.ReferenceNumber, p.Status, p.SubsanacionActiva })
                .FirstOrDefaultAsync(context.CancellationToken);
            if (existing is not null)
            {
                var existingReply = new DraftReply
                {
                    ProcedureInstanceId = existing.Id.ToString(),
                    ReferenceNumber = existing.ReferenceNumber,
                    Status = existing.Status,
                };

                // Bug #13304 — si el intento previo creó el borrador pero se cayó en actores o comercial
                // (p. ej. 22001 por un teléfono/nombre más largo que la columna), el reintento completa lo
                // que falte en vez de devolver un borrador vacío sin aviso. Solo en BORRADOR: fuera de él
                // no se reescriben partes ni precio. Mismas reglas de warning que la primera pasada.
                if (string.Equals(existing.Status, TramiteEstado.Borrador, StringComparison.Ordinal))
                {
                    await CompletarFaltantesAsync(existingReply, existing.Id, tenantId, request, context.CancellationToken);
                }

                // Bug #13109 — si el intento previo creó el borrador pero perdió los adjuntos (se cayó en
                // HandleBatchAsync), el reintento los completa aquí: sin esto el master quedaba en BORRADOR
                // con 0 adjuntos para siempre. Mismo criterio de edición que HandleBatchAsync; la dedup
                // por sha256 evita duplicar los que sí alcanzaron a quedar.
                if (request.Attachments.Count > 0
                    && TramiteEstado.PermiteEdicionDatos(existing.Status, existing.SubsanacionActiva))
                {
                    var retryCreatedBy = await ResolveIctCreatorAsync(
                        request.CreatedByUserId, tenantId, context.CancellationToken);
                    RefrescarRastreo();
                    await RegistrarAdjuntosAsync(
                        existingReply, existing.Id, tenantId, request, retryCreatedBy, context.CancellationToken);
                }

                return existingReply;
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
        Guid? officeFromIct = Guid.TryParse(request.TransitOfficeId, out var office) && office != Guid.Empty
            ? office
            : null;
        var transitOfficeId = officeFromIct;
        ResolvedTransitOffice? officeByRuntName = null;
        if (transitOfficeId is null && !string.IsNullOrWhiteSpace(request.TransitOfficeName))
        {
            officeByRuntName = await transitOfficeResolver.ResolveEnabledByNameAsync(
                tenantId, request.TransitOfficeName.Trim(), context.CancellationToken);
            transitOfficeId = officeByRuntName?.Id;
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

        // Organismo del borrador en field_values (Bug #13109, punto 1). La columna TransitOfficeId no basta:
        // finalizar/radicar (SubmitGate.OrganismoSeleccionado), el mandato y la entrega leen transit_office_*.
        // Dos orígenes: el id que mandó core-ict (código de la transacción ya validado contra catálogo +
        // grant; viaja con su código, nombre y city_code) o el OT que se acaba de resolver por el nombre RUNT
        // (trae además city_name). Un core-ict anterior a este cambio manda el id sin código: no se siembra.
        // Sin OT resuelto no se siembra nada y el gestor lo asigna, igual que antes.
        var officeToSeed = officeFromIct is { } officeId
            ? string.IsNullOrWhiteSpace(request.TransitOfficeCode)
                ? null
                : new ResolvedTransitOffice(
                    officeId, request.TransitOfficeCode.Trim(), request.TransitOfficeName.Trim(),
                    request.TransitOfficeCity.Trim())
            : officeByRuntName;
        if (officeToSeed is not null)
        {
            await SembrarOrganismoAsync(reply, summary.Id, tenantId, officeToSeed, context.CancellationToken);
        }

        // Actores del pre-trámite (vendedor/comprador + su representante legal). Se reutiliza
        // PutActorsHandler, el único escritor de partes. Fallo NO fatal: el borrador ya existe y el
        // gestor puede completarlo; se reporta como warning para no perder la trazabilidad. Bug #13304:
        // una EXCEPCIÓN (no solo un error devuelto) también es warning — antes salía como gRPC Unknown
        // con el borrador ya creado y el reintento lo devolvía sin actores ni precio.
        await AplicarActoresAsync(reply, summary.Id, tenantId, request, context.CancellationToken);

        // Datos comerciales del traspaso (valor de venta / causal / método de pago). core-ict los envía en
        // request.Commercial SOLO para traspaso (tipo 3); en el resto de trámites llega null. Se reutiliza
        // PutCommercialHandler (el MISMO escritor que el paso 5 del wizard) para que el valor de venta
        // aparezca en el borrador y el gestor lo vea. Fallo NO fatal (error o excepción): warning acumulado.
        await AplicarComercialAsync(reply, summary.Id, tenantId, request.Commercial, context.CancellationToken);

        // Adjuntos del pre-trámite: se registran por REFERENCIA (el binario ya está en el File Manager
        // corporativo; core-ict envía la metadata + storage_path + el DocTipo YA resuelto por su tabla de
        // asociación). No se re-suben bytes; se registra la misma referencia S3. Fallo NO fatal: el
        // borrador ya existe y el gestor puede completarlo; se reporta como warning acumulado.
        if (request.Attachments.Count > 0)
        {
            RefrescarRastreo();
            await RegistrarAdjuntosAsync(reply, summary.Id, tenantId, request, createdBy, context.CancellationToken);
        }

        // Preflight — PARIDAD con "Consultar RUNT del vehículo" (paso 1 del wizard manual). Un solo
        // RunPreflightHandler HIDRATA los field_values del vehículo (marca/línea/soat/rtm/gravámenes/…), corre
        // el SIMIT de comprador y vendedor, auto-vincula el OT desde el RUNT y PERSISTE el snapshot de
        // preflight. Ese snapshot es lo que el gate del paso Comprador (SIMIT) exige: sin él, el paso 4 queda
        // `incompleto` (simit_pendiente). Bug #13304 — el vehículo NO se re-consulta: se reutiliza la consulta
        // RUNT de la validación ICT (precomputed_vehicle). Best-effort: un fallo NO tumba la materialización.
        if (TieneVehiculo(request))
        {
            await CorrerPreflightIctAsync(reply, summary.Id, tenantId, request, context.CancellationToken);
        }

        // Identidad auto-iniciada — PARIDAD con el wizard manual (ensure → biométrica, TramiteWizard) y con
        // v1 (identityValidationService.validateIdentities). ICT no pasa por el wizard, así que se replica
        // aquí, DESPUÉS de materializar los actores: comprador siempre; vendedor solo en traspaso.
        // EnsureIdentity DECIDE (reutiliza una identidad vigente de la persona si existe → no revalida); solo
        // si devuelve `requiere_validacion` se inicia la biométrica del proveedor configurado (Kyverum real /
        // mock). El sujeto (nombre/doc/email) lo resuelven los handlers desde el actor. Best-effort.
        var partesIdentidad = esTraspasoDraft
            ? new[] { "comprador", "vendedor" }
            : new[] { "comprador" };
        foreach (var parte in partesIdentidad)
        {
            await AsegurarIdentidadAsync(reply, summary.Id, tenantId, parte, context.CancellationToken);
        }

        return reply;
    }

    /// <summary>
    /// Bug #13304 — rama de reintento por <c>external_ref</c>: completa lo que el intento previo no alcanzó
    /// a guardar. Sin actores → aplica los del request (y, si quedaron, vuelve a asegurar la identidad,
    /// que en la primera pasada corrió sin partes). Sin comercial o con valor 0 → aplica el del request.
    /// Lo que ya existe NO se toca (el gestor pudo haberlo editado).
    /// </summary>
    private async Task CompletarFaltantesAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, CreateDraftFromIctRequest request, CancellationToken ct)
    {
        if (request.Actors.Count > 0)
        {
            var tieneActores = await db.Set<ProcedureInstanceActor>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(a => a.ProcedureInstanceId == instanceId && a.TenantId == tenantId, ct);
            if (!tieneActores)
            {
                var aplicados = await AplicarActoresAsync(reply, instanceId, tenantId, request, ct);
                if (aplicados)
                {
                    RefrescarRastreo();
                    var esTraspaso = request.ProcedureTypeCode?.Contains("TRASPASO", StringComparison.OrdinalIgnoreCase) == true;
                    foreach (var parte in esTraspaso ? new[] { "comprador", "vendedor" } : new[] { "comprador" })
                    {
                        await AsegurarIdentidadAsync(reply, instanceId, tenantId, parte, ct);
                    }
                }
            }
        }

        await CompletarComercialAsync(reply, instanceId, tenantId, request, ct);

        // Bug #13304 (D7) — si el intento previo no alcanzó a dejar el preflight, se corre con la consulta
        // RUNT de ICT. Con snapshot existente no se toca (el gestor pudo haberlo refrescado).
        if (TieneVehiculo(request))
        {
            var tieneSnapshot = await db.Set<ProcedureInstancePreflightSnapshot>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(s => s.ProcedureInstanceId == instanceId && s.TenantId == tenantId, ct);
            if (!tieneSnapshot)
            {
                await CorrerPreflightIctAsync(reply, instanceId, tenantId, request, ct);
            }
        }
    }

    private async Task CompletarComercialAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, CreateDraftFromIctRequest request, CancellationToken ct)
    {
        if (request.Commercial is not null)
        {
            var valorActual = await db.Set<ProcedureInstanceCommercial>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.ProcedureInstanceId == instanceId && c.TenantId == tenantId)
                .Select(c => new { c.ValorVenta })
                .FirstOrDefaultAsync(ct);
            if (valorActual is null || valorActual.ValorVenta is null or <= 0)
            {
                RefrescarRastreo();
                await AplicarComercialAsync(reply, instanceId, tenantId, request.Commercial, ct);
            }
        }
    }

    private static bool TieneVehiculo(CreateDraftFromIctRequest request) =>
        FieldValueOf(request, "plate") is not null || FieldValueOf(request, "vin") is not null;

    private static string? FieldValueOf(CreateDraftFromIctRequest request, string key) =>
        request.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase)
                                 && !string.IsNullOrWhiteSpace(f.ValueText))
            ?.ValueText;

    /// <summary>
    /// Bug #13304 (D4/D7) — preflight del borrador ICT con la consulta RUNT de la validación ICT. Válida →
    /// <see cref="RunPreflightHandler"/> con ella, persistiendo el snapshot también ante bloqueo. Ausente,
    /// vencida, de otro vehículo o ilegible → NO se consulta: <c>preflight_warning:vehicle_consultation_*</c>.
    /// </summary>
    private async Task CorrerPreflightIctAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, CreateDraftFromIctRequest request, CancellationToken ct)
    {
        var maxAgeHours = configuration.GetValue("Ict:VehicleConsultationMaxAgeHours", DefaultVehicleConsultationMaxAgeHours);
        var (precomputed, motivo) = ResolverPrecomputed(
            request.PrecomputedVehicle,
            FieldValueOf(request, "plate"),
            FieldValueOf(request, "vin"),
            maxAgeHours,
            DateTimeOffset.UtcNow);
        if (precomputed is null)
        {
            AppendWarning(reply, "preflight_warning:" + motivo);
            return;
        }

        RefrescarRastreo();
        try
        {
            var (_, preflightError, _, _) = await preflightHandler.HandleAsync(
                instanceId, tenantId, precomputed, new PreflightRunOptions(PersistSnapshotOnBlock: true), ct);
            if (preflightError is not null)
            {
                AppendWarning(reply, "preflight_warning:" + preflightError);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            RefrescarRastreo();
            AppendWarning(reply, "preflight_warning:exception");
        }
    }

    /// <summary>
    /// Bug #13304 (D4) — valida la consulta RUNT que trae ICT: presente, con fecha, no más vieja que
    /// <paramref name="maxAgeHours"/> (≤ 0 ⇒ default de 24 h), del MISMO vehículo (placa si se consultó por
    /// placa, VIN si por VIN; normalizado a mayúsculas sin espacios) y deserializable. Devuelve el snapshot o
    /// el motivo (<c>vehicle_consultation_missing|expired|mismatch|invalid</c>).
    /// </summary>
    internal static (PreflightVehicleSnapshot? Snapshot, string? Motivo) ResolverPrecomputed(
        PrecomputedVehicleConsultation? precomputed,
        string? plate,
        string? vin,
        int maxAgeHours,
        DateTimeOffset now)
    {
        if (precomputed is null || string.IsNullOrWhiteSpace(precomputed.SnapshotJson))
            return (null, "vehicle_consultation_missing");

        if (precomputed.ConsultedAt is null)
            return (null, "vehicle_consultation_invalid");

        // L-1 — una fecha futura (más allá del margen de reloj) alargaría la vigencia a voluntad: inválida.
        var consultedAt = precomputed.ConsultedAt.ToDateTimeOffset();
        if (consultedAt > now + MaxConsultedAtClockSkew)
            return (null, "vehicle_consultation_invalid");

        var vigencia = TimeSpan.FromHours(maxAgeHours > 0 ? maxAgeHours : DefaultVehicleConsultationMaxAgeHours);
        if (consultedAt < now - vigencia)
            return (null, "vehicle_consultation_expired");

        bool? coincide = precomputed.Kind switch
        {
            "VehiclePlate" => MismoIdentificador(precomputed.QueriedPlate, plate),
            "VehicleVin" => MismoIdentificador(precomputed.QueriedVin, vin),
            _ => null,
        };
        if (coincide is null)
            return (null, "vehicle_consultation_invalid");
        if (coincide == false)
            return (null, "vehicle_consultation_mismatch");

        // M-1 — defensa en profundidad: del snapshot recibido solo pasan claves de vehículo (lista blanca).
        return PreflightVehicleSnapshotJson.TryDeserialize(precomputed.SnapshotJson, out var snapshot)
            ? (snapshot!.SoloClavesDeVehiculo(), null)
            : (null, "vehicle_consultation_invalid");
    }

    private static bool MismoIdentificador(string? consultado, string? delBorrador)
    {
        var a = Normalizar(consultado);
        return a.Length > 0 && string.Equals(a, Normalizar(delBorrador), StringComparison.Ordinal);
    }

    private static string Normalizar(string? valor) =>
        string.Concat((valor ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>
    /// Materializa los actores del request con <see cref="PutActorsHandler"/>. Error devuelto →
    /// <c>actors_warning:&lt;código&gt;</c>; excepción (Bug #13304) → <c>actors_warning:persist_failed</c>
    /// (o <c>:exception</c> si no fue de persistencia), log sin PII y change tracker limpio para que
    /// comercial y adjuntos puedan guardar. Devuelve <c>true</c> si quedaron actores persistidos.
    /// </summary>
    private async Task<bool> AplicarActoresAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, CreateDraftFromIctRequest request, CancellationToken ct)
    {
        var actorInputs = MapActors(request.Actors, request.ProcedureTypeCode);
        actorInputs = await CompletarRepresentantesAsync(reply, tenantId, actorInputs, ct);
        if (actorInputs.Count == 0)
        {
            return false;
        }

        try
        {
            var (_, actorsError) = await actorsHandler.HandleAsync(
                instanceId, tenantId, new PutActorsRequest(actorInputs), ct);
            if (actorsError is not null)
            {
                AppendWarning(reply, "actors_warning:" + actorsError);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (EsFalloRecuperable(ex, ct))
        {
            RefrescarRastreo();
            var code = CodigoDeFallo(ex);
            IctOrchestrationLog.StepFailed(logger, "actors", code, ex.GetType().Name, SqlStateDe(ex), instanceId);
            AppendWarning(reply, "actors_warning:" + code);
            return false;
        }
    }

    /// <summary>
    /// Persiste los datos comerciales del request con <see cref="PutCommercialHandler"/> si traen un valor
    /// de venta &gt; 0. El handler exige una causal del catálogo cerrado; el contrato v1 no la trae, así que
    /// se usa COMPRAVENTA por defecto (la causal dominante del traspaso) y el gestor puede cambiarla.
    /// Error devuelto → <c>commercial_warning:&lt;código&gt;</c>; excepción (Bug #13304) →
    /// <c>commercial_warning:persist_failed</c> con el change tracker limpio.
    /// </summary>
    private async Task AplicarComercialAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, CommercialData? commercial, CancellationToken ct)
    {
        if (commercial is null
            || !decimal.TryParse(commercial.ValorVenta, NumberStyles.Any, CultureInfo.InvariantCulture, out var valorVenta)
            || valorVenta <= 0)
        {
            return;
        }

        var causal = string.IsNullOrWhiteSpace(commercial.Causal)
            ? "COMPRAVENTA"
            : commercial.Causal.Trim().ToUpperInvariant();
        var commercialDto = new CommercialDto(
            ValorVenta: valorVenta,
            Causal: causal,
            TasaImpuesto: null,
            Derechos: null,
            MetodoPago: string.IsNullOrWhiteSpace(commercial.MetodoPago) ? null : commercial.MetodoPago.Trim());
        try
        {
            var (_, commercialError) = await commercialHandler.HandleAsync(instanceId, tenantId, commercialDto, ct);
            if (commercialError is not null)
            {
                AppendWarning(reply, "commercial_warning:" + commercialError);
            }
        }
        catch (Exception ex) when (EsFalloRecuperable(ex, ct))
        {
            RefrescarRastreo();
            var code = CodigoDeFallo(ex);
            IctOrchestrationLog.StepFailed(logger, "commercial", code, ex.GetType().Name, SqlStateDe(ex), instanceId);
            AppendWarning(reply, "commercial_warning:" + code);
        }
    }

    /// <summary>
    /// ¿La excepción de un paso NO fatal se convierte en warning? Todo salvo cancelación del llamante y
    /// OOM (que deben propagarse). Bug #13304.
    /// </summary>
    internal static bool EsFalloRecuperable(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex is not OperationCanceledException
        && ex is not OutOfMemoryException;

    /// <summary>
    /// Código estable y SIN PII para el warning: nunca el mensaje de la excepción (puede traer el valor
    /// que reventó la columna — un nombre, un teléfono). Persistencia → <c>persist_failed</c>.
    /// </summary>
    internal static string CodigoDeFallo(Exception ex) =>
        ex is DbUpdateException ? "persist_failed" : "exception";

    /// <summary>
    /// SqlState de Postgres (p.ej. 22001 = valor demasiado largo) cuando la excepción es un DbUpdateException
    /// con PostgresException interna; "-" si no aplica. Diagnóstico sin PII: nunca ex.Message.
    /// </summary>
    internal static string SqlStateDe(Exception ex) =>
        ex is DbUpdateException { InnerException: Npgsql.PostgresException pg } ? pg.SqlState : "-";

    /// <summary>
    /// Registra los adjuntos ICT por REFERENCIA en UNA unidad de trabajo (un solo SaveChanges): uno por
    /// uno reventaría por el token de concurrencia de la instancia al reincidir el AutoMark (ver
    /// RegisterIntegrationAttachmentHandler.HandleBatchAsync). Fallo NO fatal: el borrador ya existe, así
    /// que una excepción se reporta como warning y no sale como gRPC Unknown (Bug #13109).
    /// </summary>
    private async Task RegistrarAdjuntosAsync(
        DraftReply reply,
        Guid instanceId,
        Guid tenantId,
        CreateDraftFromIctRequest request,
        Guid createdBy,
        CancellationToken ct)
    {
        var attachmentInputs = request.Attachments
            .Select(att => new RegisterAttachmentInput(
                Tipo: att.DocumentType,
                Filename: att.Filename,
                Mimetype: att.MimeType,
                SizeBytes: att.SizeBytes,
                Sha256: att.Sha256,
                StoragePath: att.StoragePath))
            .ToList();
        try
        {
            var (_, attachmentWarnings) = await attachmentsHandler.HandleBatchAsync(
                instanceId, tenantId, attachmentInputs, createdBy, ct);
            if (attachmentWarnings.Count > 0)
            {
                AppendWarning(reply, "attachments_warning:" + string.Join(",", attachmentWarnings));
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            AppendWarning(reply, "attachments_warning:exception");
        }
    }

    /// <summary>
    /// Bug #13109 — descarta el grafo trackeado por los pasos previos antes de que el siguiente handler
    /// recargue la instancia. <c>row_version</c> es solo token de concurrencia y lo sube el trigger
    /// <c>tr_procedure_instances_row_version</c>: EF no lo relee, y por identity resolution la recarga
    /// devolvería la MISMA entidad con el token viejo → el UPDATE (AutoMark del checklist, preflight)
    /// afectaría 0 filas → DbUpdateConcurrencyException. Todos los pasos previos ya hicieron su
    /// SaveChanges, así que no se pierde nada. Mismo patrón que ConsolidadoCommand.ReloadAsync
    /// (repo.ResetTracking()).
    /// </summary>
    private void RefrescarRastreo() => db.ChangeTracker.Clear();

    /// <summary>
    /// Siembra los field_values del organismo con las MISMAS claves que el wizard al elegir la secretaría
    /// en el paso 1 (<c>CreateFromConsultaHandler</c>): id, código, nombre, city_code, city_name si se
    /// conoce y el origen <c>paso_1</c>, que hace que el paso del FUR muestre el organismo en firme en vez
    /// de volver a pedirlo (aquí también está en firme: lo fijó la transacción o el RUNT). En la rama por
    /// código <c>transit_office_city_name</c> no viaja por ICT y el FUR lo rellena en memoria del catálogo.
    /// <para>En traspaso con placa, el auto-bind del preflight (que corre después) vuelve a escribir id,
    /// código, nombre, city y city_name desde el RUNT con el mismo resolver: en la rama RUNT es el mismo
    /// OT (upsert idempotente, mismo contrato); la siembra garantiza el OT aunque el preflight falle.</para>
    /// <para>Va por <see cref="PatchFieldValuesHandler.HandleSystemSeedAsync"/> en un patch APARTE del
    /// general: B11 rechaza el patch completo si trae claves <c>transit_office_*</c> en traspaso estándar,
    /// y mezclarlas con vin/plate los perdería. Fallo NO fatal: <c>seed_warning:transit_office:&lt;err&gt;</c>.</para>
    /// </summary>
    private async Task SembrarOrganismoAsync(
        DraftReply reply,
        Guid instanceId,
        Guid tenantId,
        ResolvedTransitOffice office,
        CancellationToken ct)
    {
        var items = new List<FieldValueInput>
        {
            new(null, TransitOfficeFieldKeys.Id, office.Id.ToString(), null),
            new(null, TransitOfficeFieldKeys.Code, office.Code, null),
        };
        if (!string.IsNullOrWhiteSpace(office.Name))
        {
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Name, office.Name, null));
        }

        if (!string.IsNullOrWhiteSpace(office.CityCode))
        {
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.City, office.CityCode, null));
        }

        if (!string.IsNullOrWhiteSpace(office.CityName))
        {
            items.Add(new FieldValueInput(null, TransitOfficeFieldKeys.CityName, office.CityName, null));
        }

        items.Add(new FieldValueInput(
            null, TransitOfficeSelectionPolicy.OrigenFieldKey, TransitOfficeSelectionPolicy.OrigenPasoUno, null));

        try
        {
            var (_, seedError) = await patchHandler.HandleSystemSeedAsync(
                instanceId, tenantId, new Flit.Tramites.Application.UseCases.ProcedureInstances.PatchFieldValuesRequest(items), ct);
            if (seedError is not null)
            {
                AppendWarning(reply, "seed_warning:transit_office:" + seedError);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Las filas que no alcanzaron a guardarse se descartan para que el siguiente SaveChanges
            // (actores, comercial) no las reintente y tumbe el resto de la materialización.
            RefrescarRastreo();
            AppendWarning(reply, "seed_warning:transit_office:exception");
        }
    }

    /// <summary>
    /// Acumula un warning NO fatal en <c>reply.ErrorCode</c> sin pisar los previos (separados por ';').
    /// El borrador YA existe: el cliente ICT debe conservar el ProcedureInstanceId y tratar esto como
    /// aviso, no como fallo (ver IctGrpcProcedureDraftClient.CreateDraftAsync).
    /// </summary>
    private static void AppendWarning(DraftReply reply, string warning) =>
        reply.ErrorCode = string.IsNullOrEmpty(reply.ErrorCode) ? warning : reply.ErrorCode + ";" + warning;

    /// <summary>
    /// Bug #13194 (punto 4) — asegura la identidad de la parte y dispara el correo de validación con el
    /// método reutilizable <see cref="EnsureIdentityAndNotifyHandler"/>. Antes el resultado del inicio de
    /// la biométrica se descartaba: un <c>datos_incompletos</c> (RL sin correo) o un fallo de Kyverum dejaba
    /// el paso 4 en «Aún no se ha iniciado» sin rastro. Ahora cada fallo sale como
    /// <c>identity_warning:&lt;parte&gt;:&lt;código&gt;</c> en el reply y en el log (sin PII). Best-effort: el
    /// borrador ya existe y una excepción no tumba la materialización.
    /// </summary>
    private async Task AsegurarIdentidadAsync(
        DraftReply reply, Guid instanceId, Guid tenantId, string parte, CancellationToken ct)
    {
        try
        {
            var (result, error) = await identityNotifier.HandleAsync(instanceId, tenantId, parte, ct: ct);
            var code = IdentityWarningCode(result, error);
            if (code is not null)
            {
                IctOrchestrationLog.IdentityWarning(logger, code, instanceId, parte);
                AppendWarning(reply, "identity_warning:" + parte + ":" + code);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            IctOrchestrationLog.IdentityWarning(logger, "exception:" + ex.GetType().Name, instanceId, parte);
            AppendWarning(reply, "identity_warning:" + parte + ":exception");
        }
    }

    /// <summary>
    /// Código de aviso de identidad para el reply ICT, o <c>null</c> si la parte quedó cubierta (vigente,
    /// reusada, baúl, en curso) o se le envió la validación. <c>sin_actor</c> también avisa: sin sujeto de
    /// identidad (p. ej. PJ sin documento del representante) no hay a quién enviarle la validación.
    /// </summary>
    internal static string? IdentityWarningCode(EnsureIdentityAndNotifyResult? result, string? error)
    {
        if (error is not null || result is null)
        {
            return error ?? "ensure_null";
        }

        if (result.Outcome == EnsureIdentityOutcomes.SinActor)
        {
            return EnsureIdentityOutcomes.SinActor;
        }

        return result.Notificacion == IdentityNotificationOutcomes.Fallida
            ? result.NotificacionError ?? IdentityNotificationOutcomes.Fallida
            : null;
    }

    /// <summary>
    /// Bug #13194 (punto 4) — completa el representante legal de las partes PJ desde el directorio de la
    /// compañía del MISMO tenant (<see cref="RepresentanteLegalDesdeDirectorio"/>) y descarta, con aviso,
    /// las que sigan sin correo de contacto (<c>PutActorsHandler</c> lo exige). Los avisos salen como
    /// <c>identity_warning:rl_no_registrado:&lt;rol&gt;</c> / <c>identity_warning:rl_sin_correo:&lt;rol&gt;</c>.
    /// Un fallo del directorio no tumba la materialización: se sigue con lo que trajo el ICT.
    /// </summary>
    private async Task<List<ActorInput>> CompletarRepresentantesAsync(
        DraftReply reply, Guid tenantId, List<ActorInput> actores, CancellationToken ct)
    {
        var completados = actores;
        if (actores.Exists(RepresentanteLegalDesdeDirectorio.EsJuridica))
        {
            try
            {
                var resultado = await representanteDirectorio.CompletarAsync(tenantId, actores, ct);
                completados = [.. resultado.Actores];
                foreach (var aviso in resultado.Avisos)
                {
                    AppendWarning(reply, "identity_warning:" + aviso);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                RefrescarRastreo();
                IctOrchestrationLog.DirectoryWarning(logger, ex.GetType().Name);
                AppendWarning(reply, "identity_warning:directorio_rl:exception");
            }
        }

        var conCorreo = new List<ActorInput>(completados.Count);
        foreach (var actor in completados)
        {
            if (string.IsNullOrWhiteSpace(actor.Email))
            {
                AppendWarning(reply, "actors_warning:sin_correo:" + actor.Rol);
                continue;
            }

            conCorreo.Add(actor);
        }

        return conCorreo;
    }

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

        // Bug #13194 (punto 4) — «NIT» con puntos/espacios/minúsculas («n.i.t.») también es persona jurídica.
        var esNit = EsTipoNit(a.DocumentType);

        // La PJ puede llegar sin correo de la compañía: el directorio de RL del tenant lo completa después
        // (CompletarRepresentantesAsync), que es también quien la descarta con aviso si sigue sin él.
        if (string.IsNullOrWhiteSpace(a.DocumentNumber) || (!esNit && string.IsNullOrWhiteSpace(a.Email)))
        {
            return;
        }

        var personType = esNit ? "juridical" : "natural";

        result.Add(new ActorInput(
            Rol: rol,
            TipoDocumento: esNit ? "NIT" : a.DocumentType?.Trim().ToUpperInvariant() ?? string.Empty,
            NumeroDocumento: a.DocumentNumber.Trim(),
            NombreCompleto: a.FullName?.Trim() ?? string.Empty,
            Email: a.Email?.Trim() ?? string.Empty,
            Telefono: string.IsNullOrWhiteSpace(a.Phone) ? null : a.Phone.Trim(),
            Ciudad: MetaString(a.Metadata, "city"),
            Direccion: MetaString(a.Metadata, "address"),
            PersonType: personType,
            EsRepresentanteLegal: false,
            RepresentanteLegal: MapRepresentanteLegal(a.Metadata),
            Mandante: MapMandante(a.Metadata)));
    }

    /// <summary>¿El tipo de documento es NIT, tolerando puntos, espacios y mayúsculas/minúsculas?</summary>
    internal static bool EsTipoNit(string? documentType) =>
        string.Equals(
            new string((documentType ?? string.Empty).Where(char.IsLetter).ToArray()),
            "NIT",
            StringComparison.OrdinalIgnoreCase);

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
            instanceId, tenantId, TramiteEstado.Anulado, reason, changedBy, ct: context.CancellationToken);
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
    /// Bug #13304 — ICT edita el precio de venta del trámite SOLO mientras siga en borrador (decisión del
    /// usuario). Valida que la instancia sea del tenant y que su <c>external_ref</c> coincida con el
    /// pre-trámite que la originó (si no, <c>not_found</c>: no se revela la existencia de trámites
    /// ajenos). Fuera de borrador → <c>not_draft</c>. Reutiliza <see cref="PutCommercialHandler"/>, que
    /// valida el valor (&gt; 0) y la causal; se conservan la causal, método de pago, tasas y trazabilidad
    /// del avalúo que ya tuviera el comercial (el gestor pudo editarlos): solo cambia el valor de venta.
    /// </summary>
    public override async Task<DraftReply> UpdateDraftCommercial(
        UpdateDraftCommercialRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;

        if (!Guid.TryParse(request.TenantId, out var tenantId))
        {
            return new DraftReply { ErrorCode = "invalid_tenant" };
        }

        if (!Guid.TryParse(request.ProcedureInstanceId, out var instanceId))
        {
            return new DraftReply { ErrorCode = "invalid_instance" };
        }

        var externalRef = request.ExternalRef?.Trim();
        if (string.IsNullOrEmpty(externalRef))
        {
            return new DraftReply { ErrorCode = "invalid_external_ref" };
        }

        var instance = await db.Set<ProcedureInstance>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == instanceId && p.TenantId == tenantId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.ReferenceNumber, p.Status, p.ExternalRef })
            .FirstOrDefaultAsync(ct);
        if (instance is null || !string.Equals(instance.ExternalRef, externalRef, StringComparison.Ordinal))
        {
            return new DraftReply { ErrorCode = "not_found" };
        }

        if (!string.Equals(instance.Status, TramiteEstado.Borrador, StringComparison.Ordinal))
        {
            return new DraftReply
            {
                ProcedureInstanceId = instance.Id.ToString(),
                ReferenceNumber = instance.ReferenceNumber,
                Status = instance.Status,
                ErrorCode = "not_draft",
            };
        }

        if (request.Commercial is null
            || !decimal.TryParse(request.Commercial.ValorVenta, NumberStyles.Any, CultureInfo.InvariantCulture, out var valorVenta))
        {
            return new DraftReply { ErrorCode = "invalid_valor_venta" };
        }

        var actual = await db.Set<ProcedureInstanceCommercial>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProcedureInstanceId == instanceId && c.TenantId == tenantId, ct);
        var causalPedida = request.Commercial.Causal?.Trim();
        var metodoPedido = request.Commercial.MetodoPago?.Trim();
        var dto = new CommercialDto(
            ValorVenta: valorVenta,
            Causal: !string.IsNullOrEmpty(causalPedida) ? causalPedida.ToUpperInvariant() : actual?.Causal ?? "COMPRAVENTA",
            TasaImpuesto: actual?.TasaImpuesto,
            Derechos: actual?.Derechos,
            MetodoPago: !string.IsNullOrEmpty(metodoPedido) ? metodoPedido : actual?.MetodoPago,
            ValueOrigin: actual?.ValueOrigin,
            SuggestedSource: actual?.SuggestedSource,
            SuggestedValue: actual?.SuggestedValue);

        string? error;
        try
        {
            (_, error) = await commercialHandler.HandleAsync(instanceId, tenantId, dto, ct);
        }
        catch (Exception ex) when (EsFalloRecuperable(ex, ct))
        {
            error = CodigoDeFallo(ex);
            IctOrchestrationLog.StepFailed(logger, "update_commercial", error, ex.GetType().Name, SqlStateDe(ex), instanceId);
        }

        return new DraftReply
        {
            ProcedureInstanceId = instance.Id.ToString(),
            ReferenceNumber = instance.ReferenceNumber,
            Status = instance.Status,
            ErrorCode = error ?? string.Empty,
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
    private Task<Guid> ResolveIctCreatorAsync(string requestedUserId, Guid tenantId, CancellationToken ct) =>
        ResolveIctCreatorAsync(db, requestedUserId, tenantId, ct);

    /// <summary>
    /// Núcleo de <see cref="ResolveIctCreatorAsync(string, Guid, CancellationToken)"/> sobre un
    /// <see cref="FlitDbContext"/> explícito: <c>internal</c> para probarlo contra PostgreSQL real
    /// (Bug #13194 — el índice parcial de <c>uq_users_email</c>).
    /// </summary>
    internal static async Task<Guid> ResolveIctCreatorAsync(
        FlitDbContext db, string requestedUserId, Guid tenantId, CancellationToken ct)
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
            ON CONFLICT (email) WHERE deleted_at IS NULL DO NOTHING
            """, ct);

        // Bug #13194 — uq_users_email es parcial (deleted_at IS NULL): el árbitro del ON CONFLICT debe
        // repetir el predicado (sin él, 42P10) y la lectura solo puede devolver el usuario VIVO.
        return await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM identity.users WHERE email = {email} AND deleted_at IS NULL")
            .FirstAsync(ct);
    }
}

/// <summary>Logs del orquestador ICT sin PII (solo ids, parte y códigos).</summary>
internal static partial class IctOrchestrationLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "ICT: identidad sin asegurar ({Code}). Instancia {InstanceId}, parte {Parte}.")]
    public static partial void IdentityWarning(ILogger logger, string code, Guid instanceId, string parte);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ICT: no se pudo consultar el directorio de representantes legales ({ExceptionType}).")]
    public static partial void DirectoryWarning(ILogger logger, string exceptionType);

    // Bug #13304 — un paso NO fatal de la materialización lanzó: solo el paso, el código estable, el TIPO
    // de la excepción y el id (nunca ex.Message, que puede traer el valor que reventó la columna).
    [LoggerMessage(Level = LogLevel.Warning, Message = "ICT: el paso {Step} falló ({Code}, {ExceptionType}, SqlState {SqlState}). Instancia {InstanceId}.")]
    public static partial void StepFailed(ILogger logger, string step, string code, string exceptionType, string sqlState, Guid instanceId);
}
