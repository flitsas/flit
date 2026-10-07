using System.IO.Compression;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D4, CF-09/CF-10/CF-12) — arma, cifra, sube y cierra UNA parte ya reclamada:
/// <list type="number">
///   <item><b>ZIP en archivo temporal</b> (<see cref="ZipArchive"/> sobre <see cref="FileStream"/>, nunca en memoria):
///   los PDF de la parte en su orden (<c>processed_at, position</c>) con <see cref="CompressionLevel.NoCompression"/> y
///   <see cref="ConsolidadoLoteNombres.OmitidosCsv"/> con <see cref="CompressionLevel.Optimal"/>. Cada PDF se copia en
///   streaming desde el almacenamiento.</item>
///   <item><b>Plan B del snapshot (AC4)</b>: si el <c>storage_path</c> capturado no se abre se usa el adjunto ACTUAL del
///   mismo tipo (<see cref="IConsolidadoLoteAdjuntoActual"/>); si tampoco hay, el PDF no entra, el ítem va al CSV con
///   <c>adjunto_no_disponible</c> y pasa a omitido en la transacción que cierra la parte. <b>Nunca se genera un PDF
///   aquí</b>: este handler no depende de ningún generador (lo vigila un test).</item>
///   <item><b>Cifrado</b> a un segundo temporal con la DEK del lote (FLZ1, subclave por parte) y borrado inmediato del ZIP
///   en claro: el disco temporal queda en ≤ 2 × M.</item>
///   <item><b>AC6</b>: antes de subir se comprueba bajo lock que el lote siga vivo; si se canceló, la parte queda
///   <c>descartada</c> y no se sube. Después se sube en streaming y se cierra la parte condicionada a que siga
///   <c>empaquetando</c> con los intentos del reclamo (o <c>descartada</c> si el lote se canceló entretanto).</item>
///   <item>Tras cerrar se intenta la transición terminal del lote (AC2); si falla, la retoma el ciclo del carril.</item>
/// </list>
/// <para><b>Fallos:</b> DEK no desenvolvible ⇒ lote <c>fallido</c> (AC3); DEK ausente ⇒ el lote ya no está vivo y la
/// parte se descarta; cualquier otro error ⇒ se cuenta el intento (reintento o, al agotar <c>max_part_attempts</c>, lote
/// <c>fallido</c>). La cancelación del token (timeout del carril o parada del host) se propaga: la decide el carril.
/// Los temporales se borran siempre.</para>
/// <para><b>Nombres duplicados:</b> dos ítems con el mismo <c>{radicado}_{placa}.pdf</c> reciben un sufijo determinista
/// <c>_2</c>, <c>_3</c>… en el orden de la parte (ver <see cref="NombresDeEntradaZip"/>).</para>
/// Logs sin PII: ids de lote, número de parte, conteos y tipos de excepción.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var desenlace = await handler.HandleAsync(reclamada, settings.MaxPartAttempts, ct);
/// </code>
/// </remarks>
public sealed partial class EmpaquetarParteHandler(
    IConsolidadoLoteEmpaquetado empaquetado,
    IConsolidadoLoteCipher cipher,
    IConsolidadoLoteParteStorage partes,
    IAttachmentStorage adjuntos,
    IConsolidadoLoteAdjuntoActual adjuntoActual,
    ConsolidadoLoteTemporales temporales,
    ILogger<EmpaquetarParteHandler> logger)
{
    private const int Bufer = 81920;

    public async Task<EmpaquetarParteDesenlace> HandleAsync(
        ParteLoteReclamada reclamada, short maxIntentos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reclamada);
        var lote = reclamada.Lote;
        var parte = reclamada.Parte;
        string? rutaZip = null;
        string? rutaCifrado = null;
        try
        {
            var contenido = await empaquetado.LeerContenidoAsync(lote.Id, parte.PartNumber, ct).ConfigureAwait(false);
            (rutaZip, rutaCifrado) = temporales.NuevasRutas(lote.Id, parte.PartNumber);

            var noDisponibles = await ArmarZipAsync(rutaZip, lote.DocumentType, contenido, ct).ConfigureAwait(false);

            ConsolidadoLoteCifradoResultado cifrado;
            await using (var origen = AbrirLectura(rutaZip))
            await using (var destino = new FileStream(
                rutaCifrado, FileMode.CreateNew, FileAccess.Write, FileShare.None, Bufer, FileOptions.Asynchronous))
            {
                cifrado = await cipher
                    .CifrarAsync(lote.DekWrapped, lote.Id, parte.PartNumber, origen, destino, ct)
                    .ConfigureAwait(false);
            }

            // El ZIP en claro ya no hace falta: solo vive el cifrado mientras se sube (≤ 2 × M en el pico).
            ConsolidadoLoteTemporales.BorrarSilencioso(rutaZip);

            // AC6 — un lote cancelado no recibe la subida.
            if (await empaquetado.DescartarSiLoteInactivoAsync(lote.Id, parte.PartNumber, parte.Attempts, ct).ConfigureAwait(false))
            {
                LogDescartada(logger, lote.Id, parte.PartNumber);
                return EmpaquetarParteDesenlace.Descartada;
            }

            var almacenado = await partes.SubirAsync(lote.Id, parte.PartNumber, rutaCifrado, ct).ConfigureAwait(false);
            var desenlace = await empaquetado.CerrarParteAsync(
                new CierreParteLote(lote.Id, parte.PartNumber, parte.Attempts, cifrado.BytesEnClaro, almacenado, noDisponibles),
                ct).ConfigureAwait(false);

            switch (desenlace)
            {
                case CierreParteDesenlace.Cerrada:
                    LogCerrada(logger, lote.Id, parte.PartNumber, contenido.Pdfs.Count - noDisponibles.Count,
                        contenido.Omitidos.Count + noDisponibles.Count, noDisponibles.Count);
                    await IntentarFinalizarAsync(lote.Id, ct).ConfigureAwait(false);
                    return EmpaquetarParteDesenlace.Cerrada;
                case CierreParteDesenlace.Descartada:
                    // El objeto subido queda huérfano e ilegible: la cancelación destruyó la DEK.
                    partes.Delete(almacenado.StoragePath);
                    LogDescartada(logger, lote.Id, parte.PartNumber);
                    return EmpaquetarParteDesenlace.Descartada;
                default:
                    LogNoAplicada(logger, lote.Id, parte.PartNumber, parte.Attempts);
                    return EmpaquetarParteDesenlace.NoAplicada;
            }
        }
        catch (ConsolidadoLoteCifradoException ex) when (ex.Error == ConsolidadoLoteCifradoError.DekInvalida)
        {
            LogDekInvalida(logger, lote.Id, parte.PartNumber);
            await empaquetado.FallarLoteAsync(lote.Id, ConsolidadoLoteErrores.DekInvalida, ct).ConfigureAwait(false);
            return EmpaquetarParteDesenlace.LoteFallido;
        }
        catch (ConsolidadoLoteCifradoException ex) when (ex.Error == ConsolidadoLoteCifradoError.DekAusente)
        {
            // Un lote activo siempre tiene DEK (CHECK del DDL 133): sin ella, se canceló o purgó.
            if (await empaquetado.DescartarSiLoteInactivoAsync(lote.Id, parte.PartNumber, parte.Attempts, ct).ConfigureAwait(false))
            {
                LogDescartada(logger, lote.Id, parte.PartNumber);
                return EmpaquetarParteDesenlace.Descartada;
            }

            await empaquetado.FallarLoteAsync(lote.Id, ConsolidadoLoteErrores.DekInvalida, ct).ConfigureAwait(false);
            return EmpaquetarParteDesenlace.LoteFallido;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Solo el tipo: el mensaje puede citar rutas o respuestas del almacenamiento.
            var intentos = (short)(parte.Attempts + 1);
            LogFallo(logger, lote.Id, parte.PartNumber, ex.GetType().Name, intentos, maxIntentos);
            var fallo = await empaquetado
                .RegistrarFalloParteAsync(new FalloParteLote(lote.Id, parte.PartNumber, parte.Attempts, intentos, maxIntentos), ct)
                .ConfigureAwait(false);
            return fallo switch
            {
                FalloParteDesenlace.Reprogramada => EmpaquetarParteDesenlace.Reprogramada,
                FalloParteDesenlace.LoteFallido => EmpaquetarParteDesenlace.LoteFallido,
                FalloParteDesenlace.Descartada => EmpaquetarParteDesenlace.Descartada,
                _ => EmpaquetarParteDesenlace.NoAplicada,
            };
        }
        finally
        {
            ConsolidadoLoteTemporales.BorrarSilencioso(rutaZip);
            ConsolidadoLoteTemporales.BorrarSilencioso(rutaCifrado);
        }
    }

    /// <summary>
    /// Escribe el ZIP en <paramref name="ruta"/> y devuelve los ítems cuyo PDF no se pudo leer (AC4). Un error del
    /// almacenamiento (excepción, no «no existe») se propaga: es fallo técnico de la parte, no del ítem.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ArmarZipAsync(
        string ruta, string tipoDocumento, ContenidoParteLote contenido, CancellationToken ct)
    {
        var noDisponibles = new List<PdfDeParte>();
        var nombres = new NombresDeEntradaZip();

        await using (var archivo = new FileStream(
            ruta, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, Bufer, FileOptions.Asynchronous))
        {
            using (var zip = new ZipArchive(archivo, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var pdf in contenido.Pdfs)
                {
                    await using var origen = await AbrirPdfAsync(pdf, tipoDocumento, ct).ConfigureAwait(false);
                    if (origen is null)
                    {
                        noDisponibles.Add(pdf);
                        continue;
                    }

                    var entrada = zip.CreateEntry(
                        nombres.Reservar(ConsolidadoLoteNombres.Pdf(pdf.ReferenceNumber, pdf.Plate)),
                        CompressionLevel.NoCompression);
                    await using var destino = entrada.Open();
                    await origen.CopyToAsync(destino, Bufer, ct).ConfigureAwait(false);
                }

                var filas = contenido.Omitidos
                    .Select(o => (o.Position, Fila: new OmitidoCsvFila(o.ReferenceNumber, o.Plate, o.Motivo)))
                    .Concat(noDisponibles.Select(p => (p.Position, Fila: new OmitidoCsvFila(
                        p.ReferenceNumber, p.Plate, ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.AdjuntoNoDisponible)))))
                    .OrderBy(x => x.Position)
                    .Select(x => x.Fila);
                var csv = zip.CreateEntry(ConsolidadoLoteNombres.OmitidosCsv, CompressionLevel.Optimal);
                await using (var destino = csv.Open())
                {
                    OmitidosCsvWriter.Escribir(destino, filas);
                }
            }

            await archivo.FlushAsync(ct).ConfigureAwait(false);
        }

        return [.. noDisponibles.Select(p => p.ItemId)];
    }

    /// <summary>Snapshot del ítem o, si ya no existe, el adjunto actual del tipo. <c>null</c> si no hay ninguno.</summary>
    private async Task<Stream?> AbrirPdfAsync(PdfDeParte pdf, string tipoDocumento, CancellationToken ct)
    {
        var origen = await adjuntos.OpenReadAsync(pdf.StoragePath, ct).ConfigureAwait(false);
        if (origen is not null)
            return origen;

        var actual = await adjuntoActual
            .StoragePathActualAsync(pdf.ProcedureInstanceId, pdf.TenantId, tipoDocumento, ct)
            .ConfigureAwait(false);
        if (actual is null || string.Equals(actual, pdf.StoragePath, StringComparison.Ordinal))
            return null;

        return await adjuntos.OpenReadAsync(actual, ct).ConfigureAwait(false);
    }

    private async Task IntentarFinalizarAsync(Guid loteId, CancellationToken ct)
    {
        try
        {
            var finalizado = await empaquetado.FinalizarLoteAsync(loteId, ct).ConfigureAwait(false);
            if (finalizado is not null)
                LogFinalizado(logger, loteId, finalizado.Estado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La parte ya está cerrada: la transición terminal la retoma el ciclo del carril.
            LogFinalizarFallido(logger, loteId, ex.GetType().Name);
        }
    }

    private static FileStream AbrirLectura(string ruta) =>
        new(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, Bufer, FileOptions.Asynchronous | FileOptions.SequentialScan);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: parte {PartNumber} cerrada ({Pdfs} PDF, {Omitidos} omitidos, {NoDisponibles} sin adjunto disponible).")]
    private static partial void LogCerrada(ILogger logger, Guid batchId, short partNumber, int pdfs, int omitidos, int noDisponibles);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote {BatchId}: parte {PartNumber} descartada (el lote ya no está activo); no se sube.")]
    private static partial void LogDescartada(ILogger logger, Guid batchId, short partNumber);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: el cierre de la parte {PartNumber} no se aplicó (intentos del reclamo {Intentos}; otra ejecución la retomó).")]
    private static partial void LogNoAplicada(ILogger logger, Guid batchId, short partNumber, short intentos);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {BatchId}: la clave del lote no se pudo desenvolver al cifrar la parte {PartNumber}; el lote pasa a fallido.")]
    private static partial void LogDekInvalida(ILogger logger, Guid batchId, short partNumber);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Lote {BatchId}: el empaquetado de la parte {PartNumber} lanzó {ExceptionType} (intento {Intentos} de {MaxIntentos}).")]
    private static partial void LogFallo(ILogger logger, Guid batchId, short partNumber, string exceptionType, short intentos, short maxIntentos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lote {BatchId}: finalizado en {Estado}.")]
    private static partial void LogFinalizado(ILogger logger, Guid batchId, string estado);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {BatchId}: la transición terminal lanzó {ExceptionType}; la retoma el ciclo del carril.")]
    private static partial void LogFinalizarFallido(ILogger logger, Guid batchId, string exceptionType);
}

/// <summary>Desenlace de <see cref="EmpaquetarParteHandler.HandleAsync"/>.</summary>
public enum EmpaquetarParteDesenlace
{
    /// <summary>Parte <c>cerrada</c> con su binario cifrado.</summary>
    Cerrada,

    /// <summary>El lote ya no estaba vivo: parte <c>descartada</c> (AC6).</summary>
    Descartada,

    /// <summary>Otra ejecución retomó la parte: nada escrito.</summary>
    NoAplicada,

    /// <summary>Fallo técnico con intentos restantes: la parte vuelve a <c>pendiente</c>.</summary>
    Reprogramada,

    /// <summary>El lote pasó a <c>fallido</c> (intentos agotados o DEK no desenvolvible, AC3).</summary>
    LoteFallido,
}

/// <summary>
/// HU #13378 — nombres únicos de las entradas del ZIP de una parte. Decisión ante el duplicado detectado en #13377 (dos
/// ítems con el mismo <c>{radicado}_{placa}.pdf</c>): <b>sufijo determinista</b> <c>_2</c>, <c>_3</c>… antes de la
/// extensión, en el orden de la parte, y no error. Motivo: un error haría fallar la parte en cada reintento hasta dejar el
/// lote <c>fallido</c> por un dato que el usuario no puede corregir, y perdería los demás PDF. El sufijo conserva todos los
/// PDF, es estable entre reintentos (el orden de la parte es fijo: <c>processed_at, position</c>), compara sin mayúsculas
/// (un ZIP descomprimido en Windows sobreescribiría <c>a.pdf</c> con <c>A.pdf</c>) y salta los sufijos ya usados. El
/// nombre del CSV (<see cref="ConsolidadoLoteNombres.OmitidosCsv"/>) queda reservado desde el principio.
/// </summary>
/// <remarks>Uso de ejemplo: <c>var n = new NombresDeEntradaZip(); n.Reservar("R-1_ABC.pdf"); n.Reservar("R-1_ABC.pdf"); // R-1_ABC_2.pdf</c>.</remarks>
public sealed class NombresDeEntradaZip
{
    /// <summary>Nombres ocupados; el CSV de omitidos está reservado desde el principio.</summary>
    private readonly HashSet<string> _usados = new(StringComparer.OrdinalIgnoreCase) { ConsolidadoLoteNombres.OmitidosCsv };

    /// <summary>Devuelve <paramref name="nombre"/> si está libre o la primera variante <c>{base}_{n}{ext}</c> libre (n ≥ 2).</summary>
    public string Reservar(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        if (_usados.Add(nombre))
            return nombre;

        var extension = Path.GetExtension(nombre);
        var raiz = nombre[..^extension.Length];
        for (var n = 2; ; n++)
        {
            var candidato = $"{raiz}_{n}{extension}";
            if (_usados.Add(candidato))
                return candidato;
        }
    }
}
