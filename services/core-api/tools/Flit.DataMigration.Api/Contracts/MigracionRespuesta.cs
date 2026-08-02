using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Orchestration;

namespace Flit.DataMigration.Api.Contracts;

/// <summary>
/// Lo que devuelve una migración por HTTP: el mismo reporte que la consola imprime, en JSON.
/// <para>
/// <b>Es una proyección EXPLÍCITA y no una serialización de los reportes del motor.</b> Eso no es
/// ceremonia: <c>FileManagerEndpoint.AuthToken</c> y <c>V1SnapshotEndpoint.AuthToken</c> viajan
/// dentro de esos reportes, así que serializarlos tal cual mandaría tokens de producción a quien
/// tenga la llave del API. Tampoco se resuelve con <c>[JsonIgnore]</c> en el motor: el motor no
/// debe saber que existe un API.
/// </para>
/// </summary>
public sealed record MigracionRespuesta(
    OrigenDto Origen,
    YaMigradoDto? YaMigrado,
    IReadOnlyList<InstanciaDto> Instancias)
{
    /// <summary>
    /// Dónde quedó el trámite en V2 al terminar ESTA petición. Nulo en dry-run y cuando la data
    /// plana no llegó a entrar.
    /// <para>
    /// Es lo que permite a la consola web ofrecer el enlace al trámite recién migrado sin volver a
    /// preguntar: hacen falta las DOS piezas, porque la ruta de V2 es <c>/tramites/{id}</c> pero un
    /// SuperAdmin navega con el tenant explícito y sin él vería un 404 del trámite de otra empresa.
    /// </para>
    /// </summary>
    public DestinoDto? Destino { get; init; }

    /// <summary>Cierto si alguna instancia reportó problemas (equivale al exit code 1).</summary>
    public bool ConProblemas => Instancias.Any(i => i.ConProblemas);
}

/// <summary>Coordenadas del trámite en V2: las dos piezas que arman el enlace.</summary>
public sealed record DestinoDto(Guid V2Id, Guid TenantId);

/// <summary>
/// Contra qué se corrió. Es la comprobación que en consola hace la línea «Tabla V1»: 12.807 ids
/// existen en las DOS tablas de V1, así que ver la tabla y el ambiente en cada respuesta es lo
/// que delata una configuración cruzada antes de migrar el trámite de otra empresa.
/// </summary>
public sealed record OrigenDto(
    string Tramite,
    string TablaV1,
    string TipoV2,
    string Lote,
    string BaseV1,
    string BaseV2,
    long V1Id,
    bool DryRun)
{
    internal static OrigenDto From(MigrationOrigin origin, long v1Id) => new(
        origin.KindNombre,
        origin.V1Table,
        origin.ProcedureTypeCode,
        origin.BatchId,
        // Ya vienen redactadas por MigrationOrigin.Describe: "base @ host:puerto", sin contraseña.
        origin.V1Database,
        origin.V2Database,
        v1Id,
        origin.DryRun);
}

/// <summary>
/// Lo que la libreta sabía del trámite ANTES de esta petición. Nulo la primera vez.
/// <para>
/// Reintentar un CSV entero siempre fue inofensivo —los loaders devuelven <c>Skipped</c>—, pero
/// sin este bloque un reintento se lee como un no-op silencioso. Aquí se ve con qué lote y en qué
/// estado quedó, que es lo que permite relanzar una ola cortada sin recurrir a <c>--force</c>.
/// </para>
/// </summary>
public sealed record YaMigradoDto(
    Guid V2Id,
    Guid TenantId,
    string Lote,
    string EstadoFinal,
    DateTimeOffset MigradoEl,
    IReadOnlyList<string> Avisos)
{
    internal static YaMigradoDto? From(MigrationMapEntry? entry) => entry is null
        ? null
        : new(entry.V2Id, entry.TenantId, entry.BatchId, entry.FinalStatus, entry.MigratedAt, entry.Warnings);
}

/// <summary>Resultado de una instancia sobre este trámite.</summary>
public sealed record InstanciaDto(
    string Instancia,
    string Estado,
    Guid? V2Id,
    string? Motivo,
    bool ConProblemas,
    IReadOnlyDictionary<string, int> Conteos,
    IReadOnlyList<string> Avisos)
{
    internal static InstanciaDto FromData(DataInstanceReport report)
    {
        var r = report.Results.Single();
        return new(
            "datos",
            r.Status.ToString(),
            r.V2Id,
            r.Reason,
            report.HasProblems,
            new Dictionary<string, int>
            {
                ["campos"] = r.FieldCount,
                ["actores"] = r.ActorCount,
                ["eventosHistorial"] = r.HistoryCount,
            },
            r.Warnings);
    }

    internal static InstanciaDto FromAttachments(AttachmentsInstanceReport report)
    {
        var r = report.Results.Single();
        var avisos = report.UndeclaredColumns.Count == 0
            ? r.Warnings
            : [.. r.Warnings, .. report.UndeclaredColumns.Select(
                c => $"Columna de adjunto de V1 sin declarar en el mapa: {c}")];

        return new(
            "adjuntos",
            r.Status.ToString(),
            r.V2Id,
            r.Reason,
            report.HasProblems,
            new Dictionary<string, int>
            {
                ["copiados"] = r.Copied,
                ["yaMigrados"] = r.Skipped,
                ["fallidos"] = r.Failed,
                ["excluidos"] = r.Excluded,
                ["imagenesEnLaCarta"] = r.Redundant,
            },
            avisos);
    }

    internal static InstanciaDto FromDocuments(DocumentsInstanceReport report)
    {
        var r = report.Results.Single();
        var avisos = r.Issues.Count == 0
            ? r.Warnings
            : [.. r.Warnings, .. r.Issues.Select(i => $"V1 no entregó: {i}")];

        return new(
            "documentos",
            r.Status.ToString(),
            r.V2Id,
            r.Reason,
            report.HasProblems,
            new Dictionary<string, int>
            {
                ["materializados"] = r.Materialized,
                ["yaMaterializados"] = r.Skipped,
                ["fallidos"] = r.Failed,
                ["yaVenianComoAdjunto"] = r.Duplicated,
                ["identidadesMarcadas"] = r.IdentidadesMarcadas,
                ["identidadesYaMarcadas"] = r.IdentidadesExistentes,
            },
            avisos);
    }
}
