using Flit.DataMigration.V1.Loading;

namespace Flit.DataMigration.Api.Contracts;

/// <summary>
/// Qué sabe la libreta de una lista de ids, sin migrar nada.
/// <para>
/// Es el único endpoint de este host que no escribe, y existe por dos necesidades de la consola
/// web que no se pueden cubrir con el POST:
/// </para>
/// <list type="number">
/// <item>
/// Validar un CSV recién cargado — decirle a quien opera cuáles de sus veinte ids ya estaban
/// migrados ANTES de que le dé al botón, no después.
/// </item>
/// <item>
/// Reconciliar al recargar la página. El progreso vive en el navegador, así que es una creencia,
/// no un hecho; y puede haberse quedado corto (una migración que terminó en el servidor después
/// de que se cortara la conexión) o rancio (otra persona migró los mismos ids). La libreta es la
/// única fuente de verdad, y esto es la forma barata de preguntársela.
/// </item>
/// </list>
/// </summary>
public sealed record EstadoRespuesta(
    string Tramite,
    string TablaV1,
    IReadOnlyList<EstadoItemDto> Items);

/// <summary>
/// El estado de UN id. <paramref name="Migrado"/> es explícito y no se deduce de que
/// <paramref name="Destino"/> sea nulo: un booleano se lee igual desde cualquier cliente y no
/// obliga a nadie a conocer la convención.
/// </summary>
public sealed record EstadoItemDto(
    long V1Id,
    bool Migrado,
    DestinoDto? Destino,
    string? Lote,
    string? EstadoFinal,
    DateTimeOffset? MigradoEl,
    IReadOnlyList<string> Avisos)
{
    internal static EstadoItemDto From(long v1Id, MigrationMapEntry? entry) => entry is null
        ? new(v1Id, false, null, null, null, null, [])
        : new(
            v1Id,
            true,
            new DestinoDto(entry.V2Id, entry.TenantId),
            entry.BatchId,
            entry.FinalStatus,
            entry.MigratedAt,
            entry.Warnings);
}
