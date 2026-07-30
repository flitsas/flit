namespace Flit.DataMigration.V1.Source;

/// <summary>
/// Representación CANÓNICA de un trámite de V1, independiente de dónde se leyó.
/// <para>
/// Es el contrato interno del migrador: hoy lo llena <see cref="PostgresV1SourceReader"/>
/// leyendo la copia de V1; mañana podría llenarlo un lector de JSON o de un SP sin que
/// el mapeo ni la carga se enteren. Por eso las columnas viajan como diccionario y no
/// como 90 propiedades tipadas: el esquema de V1 puede crecer sin romper esto.
/// </para>
/// </summary>
public sealed class V1SourceRecord
{
    /// <summary>Id original en V1 (clave de trazabilidad hacia <c>migration_map</c>).</summary>
    public required long Id { get; init; }

    /// <summary>Tabla master de origen, p. ej. <c>vehicle_transfer_master</c>.</summary>
    public required string SourceTable { get; init; }

    /// <summary>NIT de la compañía dueña del trámite (<c>company_registered</c>), sin normalizar.</summary>
    public string? CompanyNit { get; init; }

    /// <summary>Estado actual según el MASTER. Es la fuente de verdad del estado final (ADR).</summary>
    public required int ProcessStatus { get; init; }

    /// <summary>
    /// Todas las columnas del master, con los <c>''</c> de V1 ya normalizados a <c>null</c>.
    /// V1 usa cadena vacía en vez de NULL para "sin dato"; tratarlas igual evita migrar
    /// decenas de campos y adjuntos fantasma.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Columns { get; init; }

    /// <summary>Historial de estados reconstruible, ordenado cronológicamente.</summary>
    public required IReadOnlyList<V1StatusEvent> StatusHistory { get; init; }

    /// <summary>Devuelve el valor de una columna, o <c>null</c> si no existe o está vacía.</summary>
    public string? Column(string name) =>
        Columns.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Un cambio de estado registrado en <c>vehicle_transfer_process_status</c>.
/// V1 guarda quién, cuándo, con qué rol y con qué observación: eso permite reconstruir
/// <c>procedure_instance_status_history</c> con datos reales en vez de inventados.
/// </summary>
public sealed class V1StatusEvent
{
    /// <summary>Id del estado en el catálogo de V1 (1..8 en traspaso, 1..10 en matrícula).</summary>
    public required int StatusId { get; init; }

    /// <summary>
    /// Momento del cambio, en UTC, tomado de <c>created_at</c>.
    /// <para>
    /// NO se usa <c>registrationdate</c>: esa columna mezcla zonas horarias (el evento de creación
    /// en UTC, las transiciones en hora local de Colombia), y como V2 reordena el historial por
    /// fecha al mostrarlo, migrarla sin normalizar corrompía la cronología en ~64% de los trámites.
    /// El valor original se conserva en <see cref="LegacyRegistrationDate"/>.
    /// </para>
    /// </summary>
    public required DateTime ChangedAt { get; init; }

    /// <summary>
    /// <c>registrationdate</c> tal cual está en V1, sin normalizar. Se preserva en la metadata del
    /// evento para no perder el dato de origen; no se usa para ordenar ni para poblar
    /// <c>changed_at</c>. Ver <see cref="ChangedAt"/>.
    /// </summary>
    public DateTime? LegacyRegistrationDate { get; init; }

    /// <summary>
    /// <c>id</c> de la fila en V1 (serial). Es el orden de inserción y, por tanto, el orden
    /// cronológico REAL de los eventos — a diferencia de <see cref="ChangedAt"/>.
    /// </summary>
    public long SourceId { get; init; }

    /// <summary>Usuario que hizo el cambio (<c>userregistered</c>).</summary>
    public string? UserName { get; init; }

    /// <summary>Correo del usuario (<c>mail_userregistered</c>).</summary>
    public string? UserEmail { get; init; }

    /// <summary>Rol del usuario (<c>role_userregistered</c>), p. ej. <c>Renting_Documentador</c>.</summary>
    public string? UserRole { get; init; }

    /// <summary>Observación asociada al cambio.</summary>
    public string? Observation { get; init; }
}
