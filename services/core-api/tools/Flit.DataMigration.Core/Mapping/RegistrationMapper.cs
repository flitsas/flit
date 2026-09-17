using System.Globalization;
using System.Text.Json;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Transforma una MATRÍCULA INICIAL de V1 en el grafo de entidades de V2. Función pura: no lee ni
/// escribe bases de datos.
///
/// <para>
/// Comparte con <see cref="TransferMapper"/> toda la mecánica (actores, historial, extras,
/// utilidades) vía <see cref="V1MapperShared"/>. Lo propio de matrícula es:
/// </para>
/// <list type="bullet">
///   <item>Otro catálogo de estados (<see cref="RegistrationStateMap"/>) — <b>no</b> el de traspaso.</item>
///   <item>El titular (<c>vehicle_owner_*</c>) → actor <c>comprador</c> / entidad BUYER, ordinal 1; y
///   si la matrícula es multipropietario, sus copropietarios (<c>vehicle_registration_master_actors</c>)
///   → mismos rol y entidad, ordinales 2..4 con porcentaje (ADR-0053).</item>
///   <item>Sin datos comerciales: una matrícula inicial no es una compraventa, y el wizard de
///   matrícula de V2 (5 pasos) tampoco los pide.</item>
/// </list>
/// </summary>
public static class RegistrationMapper
{
    private const string ModalidadMatricula = "matricula_inicial";
    private const string TipologiaMatricula = "matricula_inicial";

    /// <summary>
    /// Rol del titular en V2. V1 lo llama "owner", pero V2 modela al adquirente de matrícula como
    /// <c>comprador</c>: así lo tienen los trámites nativos y así lo espera <c>MatriculaGates</c>.
    /// Se sigue la convención existente de V2 en vez de inventar un rol nuevo.
    /// </summary>
    private const string ActorTitular = "comprador";

    public static MappedProcedure Map(V1SourceRecord record, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(context);

        var stateMap = RegistrationStateMap.Instance;
        var warnings = new List<string>();
        var instanceId = DeterministicGuid.ForV1Row(record.SourceTable, record.Id);
        var finalStatus = stateMap.ToV2(record.ProcessStatus);

        if (stateMap.IsAmbiguous(record.ProcessStatus))
        {
            warnings.Add(
                $"Estado V1 {record.ProcessStatus} ({stateMap.V1Name(record.ProcessStatus)}) no tiene " +
                $"equivalente exacto en V2; se mapeó a '{finalStatus}'. Decisión pendiente de negocio. " +
                "El valor original queda en el campo 'legacy_process_status'.");
        }

        var history = V1MapperShared.MapStatusHistory(
            record, stateMap, context.TenantId, context.SystemUserId, instanceId, warnings);
        var createdAt = V1MapperShared.ParseDate(record.Column("created_at")) ?? DateTimeOffset.UtcNow;

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = context.TenantId,
            ProcedureTypeId = context.ProcedureTypeId,
            // HU #12151 — el radicado ya no lo fija el migrador: lo asigna el DEFAULT de la
            // columna (secuencia global), y un valor con prefijo violaría
            // ck_procedure_instances_reference_numerico. El prefijo MIG- era trazabilidad
            // DUPLICADA: el id de V1 ya vive en migration.migration_map y el trámite queda
            // marcado con is_migrated. Consecuencia asumida: un trámite migrado deja de
            // distinguirse a simple vista en el listado; si hiciera falta, se marca en la UI
            // con is_migrated, nunca metiéndole letras al identificador.
            // Se inserta en borrador a propósito: el trigger de inmutabilidad solo permite escribir
            // field_values mientras el padre esté en borrador. El estado real se aplica al final.
            Status = TramiteEstado.Borrador,
            ChecklistEstado = "{}",
            // Organismo y cabeza de grupo: las dos columnas que deciden quién VE el trámite (bandeja
            // del OT y vista consolidada de la red). Ver el mismo bloque en TransferMapper.
            TransitOfficeId = context.TransitOffice?.Id,
            ParentTenantIdAtCreation = context.ParentTenantId,
            CreatedByUserId = context.SystemUserId,
            CreatedAt = createdAt,
            UpdatedAt = V1MapperShared.ParseDate(record.Column("updated_at")),
            DeletedAt = V1MapperShared.ParseDate(record.Column("deleted_at")),
            SubmittedAt = V1MapperShared.FirstTransitionTo(history, TramiteEstado.Preparado, TramiteEstado.Entregado),
            // Revocado es final en V2 (HU #12166) y solo se llega desde aprobado: el cierre real del
            // trámite sigue siendo la aprobación, por eso no entra aquí.
            CompletedAt = V1MapperShared.FirstTransitionTo(history, TramiteEstado.Aprobado, TramiteEstado.Anulado),
            RowVersion = 0,
            IsMigrated = true,
        };

        return new MappedProcedure
        {
            V1Id = record.Id,
            V1Table = record.SourceTable,
            Instance = instance,
            Actors = MapActors(record, context, instanceId, warnings),
            FieldValues = MapFieldValues(record, context, instanceId, createdAt, warnings),
            // Matrícula inicial no es una compraventa: no hay valor de venta ni causal que mapear.
            Commercial = null,
            StatusHistory = history,
            FinalStatus = finalStatus,
            Warnings = warnings,
        };
    }

    // ---------------------------------------------------------------- actores

    private static List<ProcedureInstanceActor> MapActors(
        V1SourceRecord record,
        MappingContext context,
        Guid instanceId,
        List<string> warnings)
    {
        var actors = new List<ProcedureInstanceActor>();

        // El titular y su representante legal comparten prefijo en matrícula: la identidad está en
        // vehicle_owner_* y el representante en vehicle_owner_*_lr (en traspaso el prefijo del
        // representante era distinto del de identidad, por eso ActorSpec los separa).
        var titular = V1MapperShared.BuildActor(
            record,
            new V1MapperShared.ActorSpec
            {
                ActorType = ActorTitular,
                Prefix = "vehicle_owner_",
                LrPrefix = "vehicle_owner_",
                EntityId = context.BuyerEntityId,
                EmailColumn = "email_owner",
            },
            context.TenantId,
            instanceId,
            warnings);

        if (titular is not null)
        {
            actors.Add(titular);
        }

        MapCoOwners(record, context, instanceId, titular, actors, warnings);

        return actors;
    }

    /// <summary>
    /// Copropietarios (ADR-0053): V2 admite hasta <see cref="MaxActorsPerRole"/> personas por rol,
    /// con <c>ordinal</c> (1 = titular principal) y <c>ownership_percentage</c>. V1 los guarda en
    /// <c>vehicle_registration_master_actors</c>, repitiendo ahí al titular del master con
    /// <c>is_solidarity_buyer = true</c> y su porcentaje (verificado en la copia de pdn del
    /// 2026-09-14: 408 matrículas, todas con exactamente 2 filas y el titular incluido).
    /// <para>
    /// El titular sigue saliendo del master (es la fuente de verdad de la identidad); de su fila
    /// en actors solo se toma el porcentaje. Los demás entran como <c>comprador</c> ordinal 2.. en
    /// el orden de inserción de V1. Sus imágenes de identidad (<c>id_attach_*</c> por actor) NO se
    /// migran en esta instancia: se avisa para que no pase por sorpresa.
    /// </para>
    /// </summary>
    private static void MapCoOwners(
        V1SourceRecord record,
        MappingContext context,
        Guid instanceId,
        ProcedureInstanceActor? titular,
        List<ProcedureInstanceActor> actors,
        List<string> warnings)
    {
        var multiOwner = string.Equals(record.Column("has_multiple_owners"), "true", StringComparison.OrdinalIgnoreCase);
        if (!multiOwner && record.CoOwners.Count == 0)
        {
            return;
        }

        if (record.CoOwners.Count == 0)
        {
            warnings.Add(
                "El trámite está marcado como MULTIPROPIETARIO en V1 (has_multiple_owners) pero la copia "
                + "de V1 no trae filas en vehicle_registration_master_actors: solo se migró el titular.");
            return;
        }

        var titularDoc = NormalizeDocument(record.Column("vehicle_owner_document_number"));
        var ordinal = 1;

        foreach (var row in record.CoOwners)
        {
            string? Col(string name) => row.TryGetValue(name, out var v) ? v : null;

            var documento = NormalizeDocument(Col("document_number"));
            var esTitular = string.Equals(Col("is_solidarity_buyer"), "true", StringComparison.OrdinalIgnoreCase)
                || (documento.Length > 0 && string.Equals(documento, titularDoc, StringComparison.Ordinal));

            if (esTitular)
            {
                if (titular is not null)
                {
                    titular.OwnershipPercentage ??= V1MapperShared.ParseDecimal(Col("ownership_percentage"));
                }

                continue;
            }

            if (documento.Length == 0)
            {
                warnings.Add("Copropietario sin documento en V1: no se migra (V2 lo exige).");
                continue;
            }

            ordinal++;
            if (ordinal > MaxActorsPerRole)
            {
                warnings.Add(
                    $"Copropietario '{documento}' descartado: V2 admite máximo {MaxActorsPerRole} personas por rol "
                    + "(ck_procedure_instance_actors_ordinal). Queda en V1 para revisión manual.");
                continue;
            }

            var rawDocumentType = Col("document_type");
            var documentType = DocumentTypeMap.ToV2(rawDocumentType, out var unknownDocumentType);
            if (unknownDocumentType)
            {
                warnings.Add(
                    $"Copropietario '{documento}': tipo de documento '{rawDocumentType ?? "(vacío)"}' "
                    + "no reconocido en V1; se asumió 'CC'.");
            }

            var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
            V1MapperShared.AddIfPresent(metadata, "ciudad", Col("city"));
            V1MapperShared.AddIfPresent(metadata, "direccion", Col("address"));
            V1MapperShared.AddIfPresent(metadata, "legacy_document_type", unknownDocumentType ? null : rawDocumentType);
            V1MapperShared.AddIfPresent(metadata, "legacy_v1_actor_id", Col("id"));

            var representanteDoc = Col("legal_representative_document_number");
            if (!string.IsNullOrWhiteSpace(representanteDoc))
            {
                var representante = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["numeroDocumento"] = representanteDoc,
                    ["tipoDocumento"] = DocumentTypeMap.ToV2(Col("legal_representative_document_type"), out _),
                };
                V1MapperShared.AddIfPresent(representante, "nombreCompleto", V1MapperShared.ComposeName(
                    Col("legal_representative_name"),
                    Col("legal_representative_first_last_name"),
                    Col("legal_representative_second_last_name")));
                V1MapperShared.AddIfPresent(representante, "email", Col("legal_representative_email"));
                V1MapperShared.AddIfPresent(representante, "telefono", Col("legal_representative_phone"));
                metadata["representanteLegal"] = representante;
            }

            var fullName = V1MapperShared.ComposeName(Col("name"), Col("first_last_name"), Col("second_last_name"));
            if (fullName.Length == 0)
            {
                warnings.Add($"Copropietario '{documento}' sin nombre en V1.");
            }

            actors.Add(new ProcedureInstanceActor
            {
                Id = DeterministicGuid.ForV1Child(
                    record.SourceTable, record.Id, $"actor:{ActorTitular}:{ordinal.ToString(CultureInfo.InvariantCulture)}"),
                TenantId = context.TenantId,
                ProcedureInstanceId = instanceId,
                ProcedureEntityId = context.BuyerEntityId,
                ActorType = ActorTitular,
                Ordinal = ordinal,
                OwnershipPercentage = V1MapperShared.ParseDecimal(Col("ownership_percentage")),
                DocumentType = documentType,
                DocumentNumber = V1MapperShared.Truncate(documento, 20),
                FullName = V1MapperShared.Truncate(fullName, 200),
                Email = Col("email"),
                Phone = Col("phone"),
                PersonType = string.Equals(documentType, "NIT", StringComparison.Ordinal)
                    ? ActorPersonTypes.Juridical
                    : ActorPersonTypes.Natural,
                EsRepresentanteLegal = false,
                Metadata = JsonSerializer.Serialize(metadata),
                CreatedAt = V1MapperShared.ParseDate(Col("created_at"))
                    ?? V1MapperShared.ParseDate(record.Column("created_at"))
                    ?? DateTimeOffset.UtcNow,
            });
        }

        var agregados = actors.Count(a => a.Ordinal > 1);
        if (agregados > 0)
        {
            warnings.Add(
                $"Matrícula MULTIPROPIETARIO: se migraron {agregados} copropietario(s) además del titular "
                + "(ordinal y porcentaje de V1). Sus imágenes de identidad por actor no se migran.");
        }
    }

    /// <summary>V2 admite hasta 4 personas por rol (ck_procedure_instance_actors_ordinal).</summary>
    private const int MaxActorsPerRole = 4;

    /// <summary>V1 guarda documentos con espacios (" 32143812"); sin esto el titular no se reconoce.</summary>
    private static string NormalizeDocument(string? value) => (value ?? string.Empty).Trim();

    // ---------------------------------------------------------------- campos (EAV)

    private static List<ProcedureInstanceFieldValue> MapFieldValues(
        V1SourceRecord record,
        MappingContext context,
        Guid instanceId,
        DateTimeOffset createdAt,
        List<string> warnings)
    {
        var values = new List<ProcedureInstanceFieldValue>();

        void Add(string fieldKey, string? text, string? json = null)
        {
            if (text is null && json is null)
            {
                return;
            }

            values.Add(new ProcedureInstanceFieldValue
            {
                Id = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, $"field:{fieldKey}"),
                TenantId = context.TenantId,
                ProcedureInstanceId = instanceId,
                FormFieldId = null,
                FieldKey = fieldKey,
                ValueText = text,
                ValueJson = json,
                Source = V1MapperShared.SourceTag,
                CreatedAt = createdAt,
            });
        }

        // El organismo resuelto contra el catálogo de V2 tiene prioridad sobre el texto de V1 (ver
        // V1MapperShared.TransitOfficeFields): se calcula antes para que el bucle no escriba dos
        // veces la misma clave (el id del field_value es determinístico por clave).
        var transitOffice = V1MapperShared.TransitOfficeFields(record, context, warnings);
        var overridden = transitOffice.Select(t => t.FieldKey).ToHashSet(StringComparer.Ordinal);

        foreach (var (column, fieldKey) in RegistrationFieldMap.FieldKeys)
        {
            if (overridden.Contains(fieldKey))
            {
                continue;
            }

            var value = record.Column(column);
            if (value is not null)
            {
                value = DecodeFieldValue(fieldKey, value, warnings);
            }

            Add(fieldKey, value);
        }

        foreach (var (fieldKey, value) in transitOffice)
        {
            Add(fieldKey, value);
        }

        // --- Trazabilidad del origen: permite auditar y recalcular sin volver a V1.
        Add("legacy_v1_id", record.Id.ToString(CultureInfo.InvariantCulture));
        Add("legacy_v1_table", record.SourceTable);
        Add("legacy_process_status", record.ProcessStatus.ToString(CultureInfo.InvariantCulture));
        Add("legacy_process_status_name", RegistrationStateMap.Instance.V1Name(record.ProcessStatus));

        // --- Adjuntos: en la instancia 1 se migra SOLO la referencia (uuid del File Manager de V1).
        // No se escriben en procedure_instance_attachments porque esa tabla exige el sha256 del
        // binario, que todavía no tenemos. La instancia 2 los resolverá desde aquí.
        var attachments = record.Columns
            .Where(kv => RegistrationFieldMap.IsAttachment(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        if (attachments.Count > 0)
        {
            Add("legacy_attachments", null, JsonSerializer.Serialize(attachments));
        }

        // --- Seguridad: columnas con secretos se descartan por completo (ni siquiera a extras).
        var sensitiveDropped = record.Columns.Count(kv =>
            RegistrationFieldMap.IsSensitive(kv.Key) && kv.Value is not null);
        if (sensitiveDropped > 0)
        {
            warnings.Add(
                $"{sensitiveDropped} columna(s) sensible(s) excluida(s) por seguridad "
                + "(headers habeas_data con token de autenticación).");
        }

        // --- Cero pérdida: toda columna con dato que no tuvo destino explícito se conserva.
        var extras = record.Columns
            .Where(kv => RegistrationFieldMap.IsExtra(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        if (extras.Count > 0)
        {
            Add("legacy_v1_extras", null, JsonSerializer.Serialize(extras));
        }

        return values;
    }

    /// <summary>
    /// Traduce a texto los campos que V1 guarda como código y normaliza el tipo de documento del
    /// titular. Mismo criterio que traspaso: un código desconocido se preserva crudo y se avisa.
    /// </summary>
    private static string DecodeFieldValue(string fieldKey, string value, List<string> warnings)
    {
        switch (fieldKey)
        {
            case "owner_document_type":
                // Comparte vocabulario con los actores: sin normalizar, el EAV diría 'N' mientras
                // el actor dice 'NIT'.
                return DocumentTypeMap.ToV2(value, out _);

            case "vehicle_fuel":
                var fuel = VehicleCodeMap.DecodeFuel(value, out var fuelUnknown);
                if (fuelUnknown)
                {
                    warnings.Add($"Combustible con código '{value}' sin equivalente en el catálogo; se dejó crudo.");
                }

                return fuel;

            case "vehicle_service":
                var service = VehicleCodeMap.DecodeService(value, out var serviceUnknown);
                if (serviceUnknown)
                {
                    warnings.Add($"Tipo de servicio con código '{value}' desconocido; se dejó crudo.");
                }

                return service;

            default:
                return value;
        }
    }
}
