using System.Globalization;
using System.Text.Json;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;

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
///   <item>Un solo titular (<c>vehicle_owner_*</c>) → actor <c>comprador</c> / entidad BUYER.</item>
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
            // Prefijo propio y distinto del de traspaso (MIG-TR-): nunca colisiona con el
            // consecutivo que genera la app y deja evidente de dónde vino el trámite.
            ReferenceNumber = $"MIG-MI-{record.Id.ToString(CultureInfo.InvariantCulture)}",
            // Se inserta en borrador a propósito: el trigger de inmutabilidad solo permite escribir
            // field_values mientras el padre esté en borrador. El estado real se aplica al final.
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = ModalidadMatricula,
            TipologiaCodigo = TipologiaMatricula,
            ChecklistEstado = "{}",
            CreatedByUserId = context.SystemUserId,
            CreatedAt = createdAt,
            UpdatedAt = V1MapperShared.ParseDate(record.Column("updated_at")),
            DeletedAt = V1MapperShared.ParseDate(record.Column("deleted_at")),
            SubmittedAt = V1MapperShared.FirstTransitionTo(history, TramiteEstado.Preparado, TramiteEstado.Entregado),
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

        // Multipropietario: V1 lo guarda en vehicle_registration_master_actors, una tabla que hoy
        // NO existe en la copia de producción (es de develop). Cuando llegue una copia fresca hay
        // que decidir cómo se modela en V2, que espera un único adquirente por matrícula.
        if (string.Equals(record.Column("has_multiple_owners"), "true", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "El trámite está marcado como MULTIPROPIETARIO en V1 (has_multiple_owners). Solo se "
                + "migró el titular principal: los copropietarios viven en "
                + "vehicle_registration_master_actors y su modelo en V2 está pendiente de definir.");
        }

        return actors;
    }

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

        foreach (var (column, fieldKey) in RegistrationFieldMap.FieldKeys)
        {
            var value = record.Column(column);
            if (value is not null)
            {
                value = DecodeFieldValue(fieldKey, value, warnings);
            }

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
