using System.Text.Json;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>Fila del historial de transiciones expuesta por la API (RF05).</summary>
public sealed record StatusHistoryItemDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    DateTimeOffset ChangedAt,
    Guid? ChangedByUserId,
    string? ChangedByName,
    string? Reason,
    // HU #12184 — compañía a la que pertenecía quien ejecutó el movimiento. No es el tenant del
    // trámite (ese es el mismo en todas las filas): es quién hizo cada paso. `null` cuando lo movió
    // un proceso automático, cuando el usuario ya no existe, o en movimientos anteriores a esta HU.
    string? ChangedByCompania = null);

/// <summary>
/// HU #14173 — modificación de un campo del trámite (evento <c>dato_modificado</c>),
/// expuesta dentro del historial de la instancia. El tiempo se guarda y se devuelve en UTC;
/// no se convierte a hora Colombia.
/// </summary>
public sealed record StatusHistoryModificationDto(
    Guid Id,
    string Etiqueta,
    string Campo,
    string Anterior,
    string Nuevo,
    DateTimeOffset ChangedAt,
    Guid? ChangedByUserId,
    string? ChangedByName,
    string? ChangedByCompania);

/// <summary>
/// Página del historial: más reciente primero.
/// <para>HU #14173 — <see cref="Modificaciones"/> es una colección ADITIVA; <c>null</c> solo si no
/// pudo cargarse (defensive). No se mezcla con <see cref="Items"/> para no pintarlos como cambios
/// de estado vacíos. <see cref="Items"/> sigue siendo el panel de estados.</para>
/// <para>HU #14176 — <see cref="Modificaciones"/> incluye además cada fila de
/// <c>procedure_instance_status_history</c> proyectada con <c>Etiqueta="Estado"</c> y
/// <c>Campo="estado"</c>; mezclada con <c>dato_modificado</c>, ordenada por ChangedAt DESC y sin
/// duplicar filas de estado que ya existan como <c>dato_modificado</c> con mismo anterior/nuevo.</para>
/// <para>HU #14180 — la misma colección proyecta, una sola vez, los actos que ya se guardan como
/// <c>ProcedureInstanceEvent</c> (reasignación de gestor, reenvío de validación y revocatoria).
/// No se escribe un segundo evento ni se abre otro historial. El tiempo sigue en UTC.</para>
/// </summary>
public sealed record StatusHistoryPageDto(
    IReadOnlyList<StatusHistoryItemDto> Items,
    int Total,
    int Page,
    int PageSize,
    // HU #14173 — colección aditiva, nunca dentro de Items.
    IReadOnlyList<StatusHistoryModificationDto>? Modificaciones = null,
    int ModificacionesTotal = 0);

/// <summary>
/// GET /instances/{id}/status-history (HU-2, RF05): historial de transiciones de estado de la
/// instancia, paginado y ordenado por <c>changed_at</c> DESC, con el nombre del usuario que
/// ejecutó cada transición resuelto contra <c>identity.users</c> (null si fue un proceso
/// automático o el usuario ya no existe).
/// <para>
/// HU #14173 — La respuesta incluye también <c>Modificaciones</c> (colección aditiva de eventos
/// <c>dato_modificado</c>). El endpoint no está documentado en
/// <c>contracts/openapi/core-api.v1.yaml</c>; cuando se documente, añadir la propiedad
/// <c>modificaciones</c> al schema de <c>StatusHistoryPageDto</c>.
/// </para>
/// </summary>
public sealed class GetStatusHistoryHandler(IProcedureInstanceRepository repo)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<(StatusHistoryPageDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var skip = (page - 1) * pageSize;

        var pageData = await repo.GetStatusHistoryPageAsync(id, tenantId, skip, pageSize, ct);
        if (pageData is null)
            return (null, TramiteEstadoErrores.NoEncontrado);

        var (entries, total) = pageData.Value;
        var items = entries
            .Select(e => new StatusHistoryItemDto(
                e.Id, e.FromStatus, e.ToStatus, e.ChangedAt, e.ChangedByUserId, ResolveActorName(e), e.Reason,
                e.ChangedByCompania))
            .ToList();

        // HU #14173 — colección ADITIVA de modificaciones de datos (dato_modificado), la misma página
        // que los estados. NO se meten en Items: el front los pinta por separado.
        var modsResult = await repo.GetModificacionesPageAsync(id, tenantId, skip, pageSize, ct);
        var dataMods = modsResult is not null
            ? modsResult.Value.Items
                .Select(m => new StatusHistoryModificationDto(
                    m.Id, m.Etiqueta, m.Campo, m.Anterior, m.Nuevo,
                    m.ChangedAt, m.ChangedByUserId,
                    // HU #14180 — consulta RUNT e identidad sin usuario: nombre de sistema, compañía vacía.
                    StatusHistoryModificacionLectura.NombreActor(m.Campo, m.ChangedByUserId, m.ChangedByName),
                    StatusHistoryModificacionLectura.CompaniaActor(m.Campo, m.ChangedByUserId, m.ChangedByCompania)))
                .ToList()
            : new List<StatusHistoryModificationDto>();
        var modTotal = modsResult?.Total ?? 0;

        // HU #14176 — cada fila de status history se proyecta también en Modificaciones como
        // Etiqueta="Estado" / Campo="estado". Se mezcla con dato_modificado y se ordena por
        // ChangedAt DESC. Un dato_modificado con campo "estado" y mismo anterior/nuevo que una
        // fila de status history se descarta para no duplicar.
        var statusMods = entries
            .Select(e => new StatusHistoryModificationDto(
                e.Id,
                "Estado",
                "estado",
                e.FromStatus ?? "",
                e.ToStatus,
                e.ChangedAt,
                e.ChangedByUserId,
                ResolveActorName(e),
                e.ChangedByCompania))
            .ToList();

        var dedupedDataMods = StatusHistoryModificacionLectura.CollapseIdentical(dataMods
            .Where(d => !(d.Campo == "estado" &&
                statusMods.Any(s => s.Anterior == d.Anterior && s.Nuevo == d.Nuevo)))
            .ToList());

        var allMods = (IReadOnlyList<StatusHistoryModificationDto>)statusMods
            .Concat(dedupedDataMods)
            .OrderByDescending(m => m.ChangedAt)
            .ThenByDescending(m => m.Id)
            .ToList();

        // Total combinado: suma de ambas fuentes (la deduplicación exacta aplica solo a la página).
        var combinedModTotal = total + modTotal;

        return (new StatusHistoryPageDto(items, total, page, pageSize, allMods, combinedModTotal), null);
    }

    private const string MigrationOrigin = "migration_v1";

    /// <summary>
    /// Migración V1→V2 — para eventos importados desde V1 (marcados con <c>origen=migration_v1</c> en
    /// el metadata) muestra el usuario REAL de V1 (<c>metadata.usuario</c>) en vez del usuario de sistema
    /// "Migración V1" resuelto por <c>changed_by</c>. Así el historial se lee transparente/idéntico a uno
    /// nativo. Para eventos nativos (o migración sin usuario) cae al nombre ya resuelto. No se altera
    /// <c>ChangedByUserId</c>: la trazabilidad técnica del changed_by real se conserva.
    /// </summary>
    private static string? ResolveActorName(ProcedureInstanceStatusHistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Metadata))
            return entry.ChangedByName;

        try
        {
            using var doc = JsonDocument.Parse(entry.Metadata);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("origen", out var origen)
                && origen.ValueKind == JsonValueKind.String
                && string.Equals(origen.GetString(), MigrationOrigin, StringComparison.Ordinal)
                && root.TryGetProperty("usuario", out var usuario)
                && usuario.ValueKind == JsonValueKind.String)
            {
                var real = usuario.GetString();
                if (!string.IsNullOrWhiteSpace(real))
                    return real;
            }
        }
        catch (JsonException)
        {
            // Metadata no parseable: se preserva el nombre resuelto por changed_by.
        }

        return entry.ChangedByName;
    }
}

/// <summary>
/// HU #14180 — lectura de la línea de Modificaciones. Proyecta los actos que ya existen como
/// <c>ProcedureInstanceEvent</c> y resuelve el actor de sistema de un <c>dato_modificado</c>
/// sin usuario. No escribe eventos. El tiempo no se convierte: entra y sale en UTC.
/// </summary>
public static class StatusHistoryModificacionLectura
{
    public const string ActorConsultaRunt = "Sistema · RUNT";
    public const string ActorIdentidad = "Sistema · Validación de identidad";

    private const string TipoDatoModificado = "dato_modificado";
    private const int MaxFrase = 2000;

    private static readonly string[] FraseKeys = ["motivo", "accion", "acción", "reason", "decision_reason"];

    private static readonly Dictionary<string, (string Etiqueta, string Campo)> Actos = new(StringComparer.Ordinal)
    {
        ["reasignar_gestor_admin"] = ("Reasignación de gestor", "reasignacion_gestor"),
        ["reenvio_validacion_admin"] = ("Reenvío de validación", "reenvio_validacion"),
        ["revocatoria_solicitada"] = ("Solicitud de revocatoria", "revocatoria"),
        ["revocatoria_aprobada"] = ("Revocatoria aprobada", "revocatoria"),
        ["revocatoria_rechazada"] = ("Revocatoria rechazada", "revocatoria"),
    };

    /// <summary>Tipos que entran en la línea de Modificaciones. Lista local para que EF la traduzca a IN.</summary>
    public static List<string> Tipos() =>
    [
        TipoDatoModificado,
        "reasignar_gestor_admin",
        "reenvio_validacion_admin",
        "revocatoria_solicitada",
        "revocatoria_aprobada",
        "revocatoria_rechazada",
    ];

    public readonly record struct Fila(
        Guid Id,
        string Tipo,
        string Etiqueta,
        string Campo,
        string Anterior,
        string Nuevo,
        DateTimeOffset ChangedAt,
        Guid? CreatedBy,
        bool EsActoAdministrativo);

    /// <summary>
    /// Arma una fila de Modificaciones. Un <c>dato_modificado</c> con payload roto se omite.
    /// Un acto conocido se muestra igual: si no hay frase segura, <c>Nuevo</c> es la etiqueta y
    /// <c>Anterior</c> queda vacío. No vuelca el JSON ni correos.
    /// </summary>
    public static bool TryProject(
        string tipo,
        Guid id,
        DateTimeOffset createdAt,
        Guid? createdBy,
        string? payload,
        out Fila fila)
    {
        fila = default;
        if (tipo == TipoDatoModificado)
        {
            if (!TryReadDatoModificado(payload, out var etiqueta, out var campo, out var anterior, out var nuevo))
                return false;
            fila = new Fila(id, tipo, etiqueta, campo, anterior, nuevo, createdAt, createdBy, false);
            return true;
        }

        if (!Actos.TryGetValue(tipo, out var map))
            return false;

        var nuevoActo = map.Etiqueta;
        var anteriorActo = "";
        var adminDoc = OpenObject(payload);
        if (adminDoc is not null)
        {
            using (adminDoc)
            {
                var root = adminDoc.RootElement;
                if (FraseSegura(root) is { } frase)
                    nuevoActo = frase;
                if (AnteriorLegible(root) is { } previo)
                    anteriorActo = previo;
            }
        }

        fila = new Fila(
            id, tipo, map.Etiqueta, map.Campo, anteriorActo, nuevoActo, createdAt, createdBy, true);
        return true;
    }

    /// <summary>
    /// El mismo acto queda una vez: mismo id, o mismo tipo + anterior + nuevo + ChangedAt.
    /// Un <c>dato_modificado</c> con el mismo campo y el mismo nuevo que un acto ya proyectado se descarta.
    /// Dos actos del mismo tipo en momentos distintos se conservan.
    /// </summary>
    public static IReadOnlyList<Fila> Dedup(IReadOnlyList<Fila> rows)
    {
        var kept = new List<Fila>(rows.Count);
        var ids = new HashSet<Guid>();
        var signatures = new HashSet<(string Tipo, string Anterior, string Nuevo, DateTimeOffset ChangedAt)>();
        foreach (var row in rows)
        {
            if (!ids.Add(row.Id))
                continue;
            if (!signatures.Add((row.Tipo, row.Anterior, row.Nuevo, row.ChangedAt)))
                continue;
            kept.Add(row);
        }

        var actos = new HashSet<(string Campo, string Nuevo)>();
        foreach (var row in kept)
        {
            if (row.EsActoAdministrativo)
                actos.Add((row.Campo, row.Nuevo));
        }

        if (actos.Count == 0)
            return kept;

        return kept.Where(r => r.EsActoAdministrativo || !actos.Contains((r.Campo, r.Nuevo))).ToList();
    }

    /// <summary>
    /// Red de seguridad de la página ya proyectada: mismo id, o misma etiqueta + campo + anterior +
    /// nuevo + ChangedAt, una sola vez. No funde dos actos con distinto ChangedAt.
    /// </summary>
    public static List<StatusHistoryModificationDto> CollapseIdentical(
        IReadOnlyList<StatusHistoryModificationDto> rows)
    {
        var kept = new List<StatusHistoryModificationDto>(rows.Count);
        var ids = new HashSet<Guid>();
        var signatures = new HashSet<(string Etiqueta, string Campo, string Anterior, string Nuevo, DateTimeOffset ChangedAt)>();
        foreach (var row in rows)
        {
            if (!ids.Add(row.Id))
                continue;
            if (!signatures.Add((row.Etiqueta, row.Campo, row.Anterior, row.Nuevo, row.ChangedAt)))
                continue;
            kept.Add(row);
        }

        return kept;
    }

    /// <summary>
    /// CreatedBy null y campo <c>consulta_runt</c> o <c>identidad</c>: nombre de sistema.
    /// Cualquier otro campo sin usuario se deja como llegó (la UI dice Sistema; aquí no se inventa).
    /// </summary>
    public static string? NombreActor(string campo, Guid? createdBy, string? nombreResuelto)
    {
        if (createdBy is not null)
            return nombreResuelto;
        if (campo == "consulta_runt")
            return ActorConsultaRunt;
        if (campo == "identidad")
            return ActorIdentidad;
        return nombreResuelto;
    }

    /// <summary>Los actores de sistema no tienen compañía. El resto conserva la que ya se resolvió.</summary>
    public static string? CompaniaActor(string campo, Guid? createdBy, string? companiaResuelta)
    {
        if (createdBy is null && (campo == "consulta_runt" || campo == "identidad"))
            return null;
        return companiaResuelta;
    }

    private static bool TryReadDatoModificado(
        string? payload,
        out string etiqueta,
        out string campo,
        out string anterior,
        out string nuevo)
    {
        etiqueta = "";
        campo = "";
        anterior = "";
        nuevo = "";
        var document = OpenObject(payload);
        if (document is null)
            return false;

        using (document)
        {
            var root = document.RootElement;
            etiqueta = ReadString(root, "etiqueta");
            campo = ReadString(root, "campo");
            anterior = ReadString(root, "anterior");
            nuevo = ReadString(root, "nuevo");
        }

        return true;
    }

    private static JsonDocument? OpenObject(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return null;
            }

            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FraseSegura(JsonElement root)
    {
        foreach (var key in FraseKeys)
        {
            if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String)
                continue;
            if (EsFraseCortaSegura(el.GetString(), out var frase))
                return frase;
        }

        return null;
    }

    private static string? AnteriorLegible(JsonElement root)
    {
        if (!root.TryGetProperty("anterior", out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        return EsFraseCortaSegura(el.GetString(), out var previo) ? previo : null;
    }

    private static bool EsFraseCortaSegura(string? text, out string frase)
    {
        frase = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length > MaxFrase)
            return false;
        if (trimmed.Contains('@'))
            return false;
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return false;
        if (Guid.TryParse(trimmed, out _))
            return false;
        if (trimmed.Contains("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        frase = trimmed;
        return true;
    }

    private static string ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";
}
