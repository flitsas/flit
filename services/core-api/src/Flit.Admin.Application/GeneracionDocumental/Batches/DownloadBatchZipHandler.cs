using System.Globalization;
using System.IO.Compression;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Batches;

/// <summary>Desenlaces de la descarga ZIP de un lote (CF-15). El endpoint los traduce a HTTP.</summary>
public enum StandaloneBatchZipOutcome
{
    /// <summary>Hay al menos un documento generado: 200 <c>application/zip</c> por streaming.</summary>
    Ok = 0,

    /// <summary>El lote no existe o es de otra compañía: 404 escueto, sin revelar nada.</summary>
    NotFound = 1,

    /// <summary>
    /// El lote existe en la compañía pero NINGUNA fila llegó a <c>generated</c>: 409 con una
    /// explicación. <b>No se entrega un ZIP vacío</b>: un archivo de 22 bytes que se abre sin nada
    /// dentro parece una descarga corrupta y no dice lo que de verdad pasó.
    /// </summary>
    NoDocuments = 2,
}

/// <summary>Lo que hay que descargar, ya resuelto y autorizado. <see cref="Entries"/> nunca sale por HTTP.</summary>
public sealed record StandaloneBatchZipPlan(
    StandaloneBatchZipOutcome Outcome,
    string Filename,
    IReadOnlyList<StandaloneDocumentBatchZipEntry> Entries);

/// <summary>
/// Descarga ZIP de los documentos de un lote (CF-15, HU #12211).
///
/// <para><b>El ZIP no se persiste</b> ni en storage ni en base de datos: no hay <c>SaveAsync</c> en
/// este archivo y no se crea ninguna fila. Se arma al vuelo, contra el cuerpo de la respuesta, y
/// deja de existir cuando la petición termina. Persistirlo obligaría a decidir cuándo invalidarlo
/// —un lote atascado que el reaper reprocesa cambia de contenido— y a cargar con un artefacto
/// derivado que nadie audita.</para>
///
/// <para><b>Cota de memoria: un PDF a la vez.</b> <see cref="ZipArchive"/> en
/// <see cref="ZipArchiveMode.Create"/> escribe hacia adelante sobre un stream no buscable, así que
/// no necesita el archivo completo en RAM. El bucle abre el binario de UNA entrada, lo copia por
/// bloques de 80 KB y lo cierra antes de abrir el siguiente: en ningún instante hay dos binarios
/// abiertos, ni un <c>MemoryStream</c> con el lote entero. Un lote de 100 PDF de 2 MB no cuesta
/// 200 MB de proceso.</para>
///
/// <para><b>Solo entran las filas en <c>generated</c></b>, y eso lo garantiza la consulta
/// (<c>ListBatchGeneratedFilesAsync</c> filtra por estado en SQL), no un <c>if</c> dentro del bucle.
/// Un lote de 9 generadas y 1 en error produce un ZIP de 9 entradas.</para>
/// </summary>
public sealed class DownloadBatchZipHandler
{
    /// <summary>Bloque de copia. Es la cota real de memoria por entrada, junto al buffer del deflate.</summary>
    private const int CopyBufferSize = 81920;

    private readonly IStandaloneDocumentBatchRepository _batches;
    private readonly IStandaloneDocumentRepository _documents;
    private readonly IStandaloneDocumentStorage _storage;

    public DownloadBatchZipHandler(
        IStandaloneDocumentBatchRepository batches,
        IStandaloneDocumentRepository documents,
        IStandaloneDocumentStorage storage)
    {
        _batches = batches ?? throw new ArgumentNullException(nameof(batches));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    /// <summary>
    /// Resuelve QUÉ se va a empaquetar antes de escribir un solo byte. Se separa de
    /// <see cref="WriteAsync"/> a propósito: una vez empieza el streaming el status ya está enviado
    /// y no se puede responder 404 ni 409. Autorización y vacíos se deciden aquí.
    /// </summary>
    public async Task<StandaloneBatchZipPlan> PrepareAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var filename = $"lote-{batchId}.zip";

        var batch = await _batches.GetByIdAsync(tenantId, batchId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            return new StandaloneBatchZipPlan(StandaloneBatchZipOutcome.NotFound, filename, []);
        }

        var entries = await _documents
            .ListBatchGeneratedFilesAsync(tenantId, batchId, cancellationToken)
            .ConfigureAwait(false);

        return entries.Count == 0
            ? new StandaloneBatchZipPlan(StandaloneBatchZipOutcome.NoDocuments, filename, [])
            : new StandaloneBatchZipPlan(StandaloneBatchZipOutcome.Ok, filename, entries);
    }

    /// <summary>
    /// Escribe el ZIP sobre <paramref name="output"/> —el cuerpo de la respuesta— una entrada a la
    /// vez. Devuelve cuántas entradas quedaron dentro.
    ///
    /// <para>Una entrada cuyo binario ya no está en storage se SALTA en vez de abortar: el usuario
    /// prefiere 8 documentos a un error, y el archivo perdido ya es visible en la tabla de ítems.</para>
    /// </summary>
    public async Task<int> WriteAsync(
        IReadOnlyList<StandaloneDocumentBatchZipEntry> entries,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(output);

        var escritas = 0;

        // leaveOpen: el dueño del cuerpo de la respuesta es ASP.NET Core, no este archivo.
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];

            var origen = await _storage
                .OpenReadAsync(entry.StoragePath, cancellationToken)
                .ConfigureAwait(false);

            if (origen is null)
            {
                continue;
            }

            await using (origen.ConfigureAwait(false))
            {
                var zipEntry = zip.CreateEntry(EntryName(entry, i), CompressionLevel.Fastest);
                await using var destino = zipEntry.Open();
                await origen.CopyToAsync(destino, CopyBufferSize, cancellationToken).ConfigureAwait(false);
            }

            // El binario se cerró antes de abrir el siguiente: esa es la cota de un PDF a la vez.
            escritas++;
        }

        return escritas;
    }

    /// <summary>
    /// Nombre dentro del ZIP. Va prefijado con el número de fila del XLSX por dos razones: dos filas
    /// del mismo lote pueden producir el MISMO nombre de archivo (mismo NIT dos veces) y un ZIP con
    /// nombres repetidos se descomprime perdiendo entradas; y el usuario necesita saber qué fila de
    /// su archivo produjo cada documento.
    /// </summary>
    private static string EntryName(StandaloneDocumentBatchZipEntry entry, int index)
    {
        var numero = (entry.RowNumber ?? index + 1).ToString("D3", CultureInfo.InvariantCulture);
        var limpio = Path.GetFileName(entry.Filename);

        if (string.IsNullOrWhiteSpace(limpio))
        {
            limpio = $"documento-{numero}.pdf";
        }

        return $"{numero}-{limpio}";
    }
}
