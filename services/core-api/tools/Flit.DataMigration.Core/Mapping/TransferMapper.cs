using System.Globalization;
using System.Text.Json;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>Datos del entorno de destino que el mapeo necesita resueltos de antemano.</summary>
public sealed class MappingContext
{
    public required Guid TenantId { get; init; }
    public required Guid ProcedureTypeId { get; init; }
    public required Guid SystemUserId { get; init; }
    public required Guid OwnerEntityId { get; init; }
    public required Guid BuyerEntityId { get; init; }
}

/// <summary>
/// Transforma un traspaso de V1 en el grafo de entidades de V2. Es una función pura:
/// no lee ni escribe bases de datos, lo que la hace trivial de testear y de auditar.
/// </summary>
public static class TransferMapper
{
    private const string SourceTag = "migration_v1";
    private const string ModalidadTraspaso = "traspaso";
    private const string TipologiaTraspaso = "traspaso_standard";

    public static MappedProcedure Map(V1SourceRecord record, MappingContext context)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<string>();
        var instanceId = DeterministicGuid.ForV1Row(record.SourceTable, record.Id);
        var finalStatus = StateMap.ToV2(record.ProcessStatus);

        if (StateMap.IsAmbiguous(record.ProcessStatus))
        {
            warnings.Add(
                $"Estado V1 {record.ProcessStatus} ({StateMap.V1Name(record.ProcessStatus)}) no tiene " +
                $"equivalente exacto en V2; se mapeó a '{finalStatus}'. Decisión pendiente de negocio. " +
                "El valor original queda en el campo 'legacy_process_status'.");
        }

        var history = V1MapperShared.MapStatusHistory(
            record, TransferStateMap.Instance, context.TenantId, context.SystemUserId, instanceId, warnings);
        var createdAt = V1MapperShared.ParseDate(record.Column("created_at")) ?? DateTimeOffset.UtcNow;

        var instance = new ProcedureInstance
        {
            Id = instanceId,
            TenantId = context.TenantId,
            ProcedureTypeId = context.ProcedureTypeId,
            // Prefijo propio: nunca colisiona con el consecutivo TRM-{año}-{n} que genera la app
            // y deja evidente, a simple vista, que el trámite vino de la migración.
            ReferenceNumber = $"MIG-TR-{record.Id.ToString(CultureInfo.InvariantCulture)}",
            // Se inserta en borrador a propósito: el trigger de inmutabilidad solo permite
            // escribir field_values mientras el padre esté en borrador. El estado real se
            // aplica al final (ver ProcedureInstanceLoader).
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = ModalidadTraspaso,
            TipologiaCodigo = TipologiaTraspaso,
            ChecklistEstado = "{}",
            CreatedByUserId = context.SystemUserId,
            CreatedAt = createdAt,
            UpdatedAt = V1MapperShared.ParseDate(record.Column("updated_at")),
            DeletedAt = V1MapperShared.ParseDate(record.Column("deleted_at")),
            SubmittedAt = V1MapperShared.FirstTransitionTo(history, TramiteEstado.Preparado, TramiteEstado.Entregado),
            CompletedAt = V1MapperShared.FirstTransitionTo(history, TramiteEstado.Aprobado, TramiteEstado.Anulado),
            RowVersion = 0,
            // Marca de trámite histórico importado (foto de solo lectura). En estado terminal, el
            // wizard de V2 lo muestra íntegro sin someterlo al gating vivo (ver GetWizardStateHandler).
            IsMigrated = true,
        };

        return new MappedProcedure
        {
            V1Id = record.Id,
            V1Table = record.SourceTable,
            Instance = instance,
            Actors = MapActors(record, context, instanceId, warnings),
            FieldValues = MapFieldValues(record, context, instanceId, createdAt, warnings),
            Commercial = MapCommercial(record, context, instanceId),
            StatusHistory = history,
            FinalStatus = finalStatus,
            Warnings = warnings,
        };
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
                Source = SourceTag,
                CreatedAt = createdAt,
            });
        }

        foreach (var (column, fieldKey) in TransferFieldMap.FieldKeys)
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
        Add("legacy_process_status_name", StateMap.V1Name(record.ProcessStatus));

        // --- Adjuntos: en la instancia 1 se migra SOLO la referencia (uuid del File Manager
        // de V1). No se escriben en procedure_instance_attachments porque esa tabla exige
        // sha256 del binario, que todavía no tenemos: ensuciarla con datos incompletos haría
        // pasar por válido un adjunto que no lo es. La instancia 2 los resolverá desde aquí.
        var attachments = record.Columns
            .Where(kv => TransferFieldMap.IsAttachment(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        if (attachments.Count > 0)
        {
            Add("legacy_attachments", null, JsonSerializer.Serialize(attachments));
        }

        // --- Seguridad: columnas con secretos se descartan por completo (ni siquiera a extras).
        var sensitiveDropped = record.Columns.Count(kv => TransferFieldMap.IsSensitive(kv.Key) && kv.Value is not null);
        if (sensitiveDropped > 0)
        {
            warnings.Add(
                $"{sensitiveDropped} columna(s) sensible(s) excluida(s) por seguridad " +
                "(headers habeas_data con token de autenticación).");
        }

        // --- Cero pérdida: toda columna con dato que no tuvo destino explícito se conserva
        // (excepto las sensibles, ya filtradas por IsExtra).
        var extras = record.Columns
            .Where(kv => TransferFieldMap.IsExtra(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        if (extras.Count > 0)
        {
            Add("legacy_v1_extras", null, JsonSerializer.Serialize(extras));
        }

        return values;
    }

    /// <summary>
    /// Traduce a texto los campos que V1 guarda como código (combustible, servicio) y normaliza
    /// el tipo de documento del propietario. Un código desconocido se preserva crudo y se avisa.
    /// </summary>
    private static string DecodeFieldValue(string fieldKey, string value, List<string> warnings)
    {
        switch (fieldKey)
        {
            case "owner_document_type":
                // Comparte vocabulario con los actores: si no se normaliza, el EAV diría 'N'
                // mientras el actor dice 'NIT'.
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

    // ---------------------------------------------------------------- comercial

    /// <summary>
    /// Datos comerciales del traspaso (1:1 con la instancia). V1 persiste el valor de la compraventa
    /// en <c>price_buy_sell</c>; sin valor no se crea la fila (el trámite original no lo tenía), con
    /// él el paso "comercial" del wizard de V2 queda con contenido real y deja de bloquear el gating.
    /// La causal se fija a <c>COMPRAVENTA</c> (V1 no persiste una explícita; es el flujo estándar y
    /// coincide con los trámites nativos de V2).
    /// </summary>
    private static ProcedureInstanceCommercial? MapCommercial(
        V1SourceRecord record, MappingContext context, Guid instanceId)
    {
        var valorVenta = V1MapperShared.ParseDecimal(record.Column("price_buy_sell"));
        if (valorVenta is null || valorVenta <= 0m)
        {
            return null;
        }

        return new ProcedureInstanceCommercial
        {
            Id = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, "commercial"),
            TenantId = context.TenantId,
            ProcedureInstanceId = instanceId,
            ValorVenta = valorVenta,
            Causal = "COMPRAVENTA",
            CreatedAt = V1MapperShared.ParseDate(record.Column("created_at")) ?? DateTimeOffset.UtcNow,
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

        // El representante legal vive en columnas {lrPrefix}*_lr, con prefijo DISTINTO al de identidad:
        // el vendedor se identifica con vehicle_owner_* pero su LR está en vehicle_seller_*_lr.
        AddActor("vendedor", "vehicle_owner_", "vehicle_seller_", context.OwnerEntityId, "email_seller");
        AddActor("comprador", "vehicle_buyer_", "vehicle_buyer_", context.BuyerEntityId, "email_buyer");

        return actors;

        void AddActor(string actorType, string prefix, string lrPrefix, Guid entityId, string emailColumn)
        {
            var documentNumber = record.Column($"{prefix}document_number");
            if (documentNumber is null)
            {
                warnings.Add($"Actor '{actorType}' sin documento en V1: no se migra (V2 lo exige).");
                return;
            }

            var fullName = V1MapperShared.ComposeName(
                record.Column($"{prefix}name"),
                record.Column($"{prefix}first_last_name"),
                record.Column($"{prefix}second_last_name"));

            if (fullName.Length == 0)
            {
                warnings.Add($"Actor '{actorType}' ({documentNumber}) sin nombre en V1.");
            }

            // Dirección y ciudad viven en el metadata jsonb, igual que en los trámites
            // creados por la propia app de V2.
            var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
            V1MapperShared.AddIfPresent(metadata, "ciudad", record.Column($"{prefix}city"));
            V1MapperShared.AddIfPresent(metadata, "direccion", record.Column($"{prefix}address"));

            // Representante legal / apoderado (persona jurídica): V2 lo guarda como objeto anidado
            // metadata.representanteLegal. Se arma desde las columnas {lrPrefix}*_lr de V1 (vacías
            // para personas naturales, así que solo aparece cuando realmente hay LR).
            var representanteLegal = V1MapperShared.BuildRepresentanteLegal(record, lrPrefix);
            if (representanteLegal is not null)
            {
                metadata["representanteLegal"] = representanteLegal;
            }

            var rawDocumentType = record.Column($"{prefix}document_type");
            var documentType = DocumentTypeMap.ToV2(rawDocumentType, out var unknownDocumentType);
            if (unknownDocumentType)
            {
                warnings.Add(
                    $"Actor '{actorType}': tipo de documento '{rawDocumentType ?? "(vacío)"}' " +
                    "no reconocido en V1; se asumió 'CC'.");
            }
            else
            {
                // El valor original se conserva por si el mapeo hay que revisarlo después.
                V1MapperShared.AddIfPresent(metadata, "legacy_document_type", rawDocumentType);
            }

            actors.Add(new ProcedureInstanceActor
            {
                Id = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, $"actor:{actorType}"),
                TenantId = context.TenantId,
                ProcedureInstanceId = instanceId,
                ProcedureEntityId = entityId,
                ActorType = actorType,
                DocumentType = documentType,
                DocumentNumber = V1MapperShared.Truncate(documentNumber, 20),
                FullName = V1MapperShared.Truncate(fullName, 200),
                Email = record.Column(emailColumn),
                Phone = record.Column($"{prefix}phone"),
                // Derivado del tipo de documento ya normalizado: NIT ⇒ persona jurídica, resto natural.
                // El wizard/visor de V2 lo usa para el bloque de representante legal.
                PersonType = string.Equals(documentType, "NIT", StringComparison.Ordinal)
                    ? ActorPersonTypes.Juridical
                    : ActorPersonTypes.Natural,
                EsRepresentanteLegal = false,
                Metadata = JsonSerializer.Serialize(metadata),
                CreatedAt = V1MapperShared.ParseDate(record.Column("created_at")) ?? DateTimeOffset.UtcNow,
            });
        }
    }

}
