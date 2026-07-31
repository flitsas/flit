using System.Globalization;
using System.Text.Json;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.DataMigration.V1.Mapping;

/// <summary>
/// Mecánica común a todos los tipos de trámite de V1 (traspaso, matrícula, …).
///
/// <para>
/// Lo que vive aquí es lo que NO cambia entre tipos: cómo se compone un nombre, cómo se arma el
/// representante legal desde las columnas <c>*_lr</c>, cómo se reconstruye el historial de estados,
/// cómo se parsean fechas y decimales de V1. Lo que SÍ cambia —el catálogo de estados, el mapa de
/// campos, qué actores existen— se inyecta o vive en el mapper de cada tipo.
/// </para>
///
/// <para>
/// La alternativa era copiar estas ~150 líneas por tipo de trámite. Se descartó: un arreglo en el
/// parseo de fechas o en el representante legal tiene que valer para todos, no para el que se
/// acuerde de replicarlo.
/// </para>
/// </summary>
public static class V1MapperShared
{
    /// <summary>Etiqueta de origen en <c>source</c> / metadata. Común a todos los tipos.</summary>
    public const string SourceTag = "migration_v1";

    // ---------------------------------------------------------------- actores

    /// <summary>Datos para construir un actor desde las columnas de V1.</summary>
    public sealed class ActorSpec
    {
        /// <summary>Rol en V2: <c>vendedor</c>, <c>comprador</c>…</summary>
        public required string ActorType { get; init; }

        /// <summary>Prefijo de las columnas de identidad, p. ej. <c>vehicle_owner_</c>.</summary>
        public required string Prefix { get; init; }

        /// <summary>
        /// Prefijo de las columnas del representante legal (<c>{LrPrefix}*_lr</c>). Puede diferir del
        /// de identidad: en traspaso el vendedor se identifica con <c>vehicle_owner_</c> pero su
        /// representante vive en <c>vehicle_seller_*_lr</c>.
        /// </summary>
        public required string LrPrefix { get; init; }

        /// <summary>Entidad del catálogo de V2 (OWNER, BUYER…).</summary>
        public required Guid EntityId { get; init; }

        /// <summary>Columna con el correo, que en V1 no sigue el prefijo (<c>email_buyer</c>…).</summary>
        public required string EmailColumn { get; init; }
    }

    /// <summary>
    /// Construye un actor de V2 desde las columnas de V1. Devuelve <c>null</c> —con aviso— cuando
    /// falta el documento, que V2 exige.
    /// </summary>
    public static ProcedureInstanceActor? BuildActor(
        V1SourceRecord record,
        ActorSpec spec,
        Guid tenantId,
        Guid instanceId,
        List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(warnings);

        var documentNumber = record.Column($"{spec.Prefix}document_number");
        if (documentNumber is null)
        {
            warnings.Add($"Actor '{spec.ActorType}' sin documento en V1: no se migra (V2 lo exige).");
            return null;
        }

        var fullName = ComposeName(
            record.Column($"{spec.Prefix}name"),
            record.Column($"{spec.Prefix}first_last_name"),
            record.Column($"{spec.Prefix}second_last_name"));

        if (fullName.Length == 0)
        {
            warnings.Add($"Actor '{spec.ActorType}' ({documentNumber}) sin nombre en V1.");
        }

        // Dirección y ciudad viven en el metadata jsonb, igual que en los trámites nativos de V2.
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        AddIfPresent(metadata, "ciudad", record.Column($"{spec.Prefix}city"));
        AddIfPresent(metadata, "direccion", record.Column($"{spec.Prefix}address"));

        var representanteLegal = BuildRepresentanteLegal(record, spec.LrPrefix);
        if (representanteLegal is not null)
        {
            metadata["representanteLegal"] = representanteLegal;
        }

        var rawDocumentType = record.Column($"{spec.Prefix}document_type");
        var documentType = DocumentTypeMap.ToV2(rawDocumentType, out var unknownDocumentType);
        if (unknownDocumentType)
        {
            warnings.Add(
                $"Actor '{spec.ActorType}': tipo de documento '{rawDocumentType ?? "(vacío)"}' " +
                "no reconocido en V1; se asumió 'CC'.");
        }
        else
        {
            AddIfPresent(metadata, "legacy_document_type", rawDocumentType);
        }

        return new ProcedureInstanceActor
        {
            Id = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, $"actor:{spec.ActorType}"),
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ProcedureEntityId = spec.EntityId,
            ActorType = spec.ActorType,
            DocumentType = documentType,
            DocumentNumber = Truncate(documentNumber, 20),
            FullName = Truncate(fullName, 200),
            Email = record.Column(spec.EmailColumn),
            Phone = record.Column($"{spec.Prefix}phone"),
            // Derivado del tipo de documento ya normalizado: NIT ⇒ jurídica, resto natural.
            PersonType = string.Equals(documentType, "NIT", StringComparison.Ordinal)
                ? ActorPersonTypes.Juridical
                : ActorPersonTypes.Natural,
            EsRepresentanteLegal = false,
            Metadata = JsonSerializer.Serialize(metadata),
            CreatedAt = ParseDate(record.Column("created_at")) ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Arma el representante legal / apoderado del actor jurídico desde las columnas
    /// <c>{lrPrefix}*_lr</c> de V1, con el mismo shape que usan los trámites nativos de V2
    /// (<c>metadata.representanteLegal</c>). <c>null</c> si no hay documento de representante
    /// (p. ej. persona natural), para no ensuciar el metadata con un objeto vacío.
    /// </summary>
    public static Dictionary<string, string>? BuildRepresentanteLegal(V1SourceRecord record, string lrPrefix)
    {
        ArgumentNullException.ThrowIfNull(record);

        var numeroDocumento = record.Column($"{lrPrefix}document_number_lr");
        if (string.IsNullOrWhiteSpace(numeroDocumento))
        {
            return null;
        }

        var nombreCompleto = ComposeName(
            record.Column($"{lrPrefix}name_lr"),
            record.Column($"{lrPrefix}first_last_name_lr"),
            record.Column($"{lrPrefix}second_last_name_lr"));

        var representante = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["numeroDocumento"] = numeroDocumento,
            ["tipoDocumento"] = DocumentTypeMap.ToV2(record.Column($"{lrPrefix}document_type_lr"), out _),
        };
        AddIfPresent(representante, "nombreCompleto", nombreCompleto);
        AddIfPresent(representante, "email", record.Column($"{lrPrefix}email_lr"));
        AddIfPresent(representante, "telefono", record.Column($"{lrPrefix}phone_lr"));
        return representante;
    }

    // ---------------------------------------------------------------- historial

    /// <summary>
    /// Reconstruye el historial de estados desde los eventos de V1, traducidos con el catálogo del
    /// tipo de trámite.
    ///
    /// <para>
    /// Los eventos llegan en el orden de inserción de V1 (por <c>id</c>) y se respetan tal cual:
    /// <b>no se reordenan por fecha</b>, porque las fechas de V1 mezclan zonas horarias (el evento
    /// de creación en UTC, las transiciones en hora local de Colombia). Ordenar por fecha manda el
    /// Draft al final y corrompe la cronología de la mayoría de los trámites.
    /// </para>
    ///
    /// <para>
    /// Reporta tres situaciones, ninguna corregida en silencio: el desfase de zona horaria, que el
    /// último evento no coincida con el master (el MASTER manda, por ADR) y que no haya historial.
    /// </para>
    /// </summary>
    public static List<ProcedureInstanceStatusHistory> MapStatusHistory(
        V1SourceRecord record,
        IV1StateMap stateMap,
        Guid tenantId,
        Guid systemUserId,
        Guid instanceId,
        List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(stateMap);
        ArgumentNullException.ThrowIfNull(warnings);

        var history = new List<ProcedureInstanceStatusHistory>();
        string? previous = null;
        var index = 0;

        // Sin OrderBy: el lector ya los entrega por id (orden de inserción = cronología real).
        foreach (var evento in record.StatusHistory)
        {
            var to = stateMap.ToV2(evento.StatusId);

            // El usuario de V1 es un correo, no un uuid de identity.users: no se puede poner en
            // changed_by sin inventar un id. Se preserva íntegro en metadata, que para eso existe.
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origen"] = SourceTag,
                ["legacy_status_id"] = evento.StatusId.ToString(CultureInfo.InvariantCulture),
                ["legacy_status_name"] = stateMap.V1Name(evento.StatusId),
            };
            AddIfPresent(metadata, "usuario", evento.UserName);
            AddIfPresent(metadata, "usuario_email", evento.UserEmail);
            AddIfPresent(metadata, "usuario_rol", evento.UserRole);

            // `changed_at` sale de created_at (UTC). Se guarda también el registrationdate crudo de
            // V1 para que la normalización sea auditable y reversible: cero pérdida de información.
            if (evento.LegacyRegistrationDate is { } legacy)
            {
                metadata["legacy_registrationdate"] =
                    legacy.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            }

            history.Add(new ProcedureInstanceStatusHistory
            {
                Id = DeterministicGuid.ForV1Child(
                    record.SourceTable, record.Id, $"history:{index.ToString(CultureInfo.InvariantCulture)}"),
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                FromStatus = previous,
                ToStatus = to,
                ChangedAt = new DateTimeOffset(evento.ChangedAt, TimeSpan.Zero),
                ChangedBy = systemUserId,
                Reason = evento.Observation,
                Metadata = JsonSerializer.Serialize(metadata),
            });

            previous = to;
            index++;
        }

        // ADR: el MASTER manda. Si el último evento no coincide con el estado actual del master
        // (ocurre en ~23% de los traspasos de V1), NO se inventa un evento de cierre: se migra el
        // historial tal cual y se reporta la divergencia.
        var expected = stateMap.ToV2(record.ProcessStatus);
        if (previous is not null && !string.Equals(previous, expected, StringComparison.Ordinal))
        {
            warnings.Add(
                $"Historial divergente: el último evento es '{previous}' pero el master dice " +
                $"'{expected}'. Se respeta el master como estado final y el historial se migra sin alterar.");
        }

        if (history.Count == 0)
        {
            warnings.Add("El trámite no tiene historial de estados en V1; se migra sin línea temporal.");
        }

        AvisarDesfaseHorario(record, warnings);

        return history;
    }

    /// <summary>
    /// Avisa cuando, aun usando <c>created_at</c>, el orden por fecha sigue sin coincidir con el
    /// orden de inserción.
    ///
    /// <para>
    /// El caso general ya está resuelto en el lector: <c>changed_at</c> sale de <c>created_at</c>,
    /// que es siempre UTC, en vez de <c>registrationdate</c>, que mezcla zonas. Lo que queda es el
    /// residuo: 2 traspasos y 31 matrículas de pdn donde dos filas se insertaron con un retraso
    /// mayor que la diferencia real entre los eventos. Ahí el orden de inserción sigue siendo la
    /// verdad y es lo que se respeta, pero conviene decirlo en el reporte en vez de callarlo.
    /// </para>
    /// </summary>
    private static void AvisarDesfaseHorario(V1SourceRecord record, List<string> warnings)
    {
        var eventos = record.StatusHistory;
        if (eventos.Count < 2)
        {
            return;
        }

        var ordenPorFechaDifiere = eventos
            .Select((e, i) => (e.ChangedAt, i))
            .OrderBy(x => x.ChangedAt)
            .Select(x => x.i)
            .SequenceEqual(Enumerable.Range(0, eventos.Count)) == false;

        if (ordenPorFechaDifiere)
        {
            warnings.Add(
                "El historial tiene eventos cuya fecha de inserción no respeta el orden de "
                + "inserción (caso raro: le pasa a 33 trámites en toda la base de pdn). Se migró en "
                + "el orden de V1, que es el cronológico real.");
        }
    }

    // ---------------------------------------------------------------- utilidades

    public static DateTimeOffset? FirstTransitionTo(
        IEnumerable<ProcedureInstanceStatusHistory> history,
        params string[] statuses)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(statuses);

        return history.Where(h => statuses.Contains(h.ToStatus, StringComparer.Ordinal))
                      .Select(h => (DateTimeOffset?)h.ChangedAt)
                      .FirstOrDefault();
    }

    public static string ComposeName(params string?[] parts) =>
        string.Join(' ', (parts ?? []).Where(p => !string.IsNullOrWhiteSpace(p))).Trim();

    public static void AddIfPresent(Dictionary<string, string> target, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    /// <summary>Overload para metadata con objetos anidados (p. ej. <c>metadata.representanteLegal</c>).</summary>
    public static void AddIfPresent(Dictionary<string, object> target, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    public static string Truncate(string value, int max)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= max ? value : value[..max];
    }

    public static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
