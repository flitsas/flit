using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary><c>GET /api/v1/consolidados/lotes/{loteId}/partes/{numero}</c>. Dueño, rol, IP y navegador salen de la petición.</summary>
public sealed record DescargarParteQuery(
    Guid LoteId,
    int Numero,
    Guid UsuarioId,
    string RolCodigo,
    IPAddress? ClientIp = null,
    string? UserAgent = null);

/// <summary>Desenlace de <see cref="DescargarParteHandler.PrepararAsync"/>.</summary>
public enum DescargarParteEstado
{
    /// <summary>Validada, auditada y con el objeto cifrado abierto: lista para escribir.</summary>
    Lista,

    /// <summary>404: el lote no existe, no es del usuario, no tiene partes (fallido/cancelado) o la parte no existe.</summary>
    NoEncontrada,

    /// <summary>409 <c>lote_no_terminado</c>.</summary>
    NoTerminado,

    /// <summary>410 <c>descarga_expirada</c>.</summary>
    Expirada,

    /// <summary>503 <c>auditoria_no_registrada</c>: sin bytes.</summary>
    AuditoriaNoRegistrada,

    /// <summary>500 <c>parte_no_disponible</c>: el objeto cifrado no se pudo abrir. Sin bytes.</summary>
    ParteNoDisponible,
}

/// <summary>Resultado de <see cref="DescargarParteHandler.PrepararAsync"/>; <see cref="Descarga"/> solo en <see cref="DescargarParteEstado.Lista"/>.</summary>
public sealed record DescargarParteResultado(DescargarParteEstado Estado, DescargaParte? Descarga = null);

/// <summary>
/// Descarga preparada: el objeto cifrado ya abierto (en streaming) y lo necesario para las cabeceras. Quien la recibe la
/// dispone (cierra la conexión con el almacenamiento).
/// </summary>
public sealed class DescargaParte : IAsyncDisposable
{
    internal DescargaParte(ConsolidadoExportBatch lote, ConsolidadoExportBatchPart parte, string nombreArchivo, long bytesEnClaro, Stream cifrado)
    {
        Lote = lote;
        Parte = parte;
        NombreArchivo = nombreArchivo;
        BytesEnClaro = bytesEnClaro;
        Cifrado = cifrado;
    }

    public ConsolidadoExportBatch Lote { get; }

    public ConsolidadoExportBatchPart Parte { get; }

    /// <summary>Nombre del ZIP (CF-13) para <c>Content-Disposition</c>.</summary>
    public string NombreArchivo { get; }

    /// <summary><c>plain_size_bytes</c>: el <c>Content-Length</c> que se fija antes del primer byte.</summary>
    public long BytesEnClaro { get; }

    internal Stream Cifrado { get; }

    public ValueTask DisposeAsync() => Cifrado.DisposeAsync();
}

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4/D8, CF-11/CF-12/CF-13/CF-22) — <c>DescargarParteQuery</c>, en dos tiempos para
/// que el endpoint pueda fijar las cabeceras entre uno y otro:
/// <list type="number">
///   <item><see cref="PrepararAsync"/>: dueño (<c>sub</c>), estado (<see cref="ConsolidadoLoteDescargabilidad"/>), parte
///   <c>cerrada</c>, apertura del objeto cifrado en streaming y la fila <c>parte_descargada</c> — síncrona y ANTES del
///   primer byte; si no se registra, 503 sin bytes.</item>
///   <item><see cref="EscribirAsync"/>: descifrado en streaming hacia el destino (la respuesta HTTP), bloque a bloque;
///   solo se escriben bloques autenticados. Si el total no coincide con <c>plain_size_bytes</c> lanza
///   <see cref="ConsolidadoLoteCifradoException"/> (el endpoint aborta la conexión).</item>
/// </list>
/// <b>Nunca genera un PDF</b>: no depende de ningún generador (lo vigila un test de dependencias).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var r = await handler.PrepararAsync(new DescargarParteQuery(loteId, 1, sub, "Radicador"), ct);
/// if (r.Estado == DescargarParteEstado.Lista)
///     await using (r.Descarga!) await handler.EscribirAsync(r.Descarga!, response.Body, ct);
/// </code>
/// </remarks>
public sealed partial class DescargarParteHandler(
    IConsolidadoLoteLectura lectura,
    IConsolidadoLoteParteStorage almacenamiento,
    IConsolidadoLoteCipher cifrador,
    ILogger<DescargarParteHandler>? logger = null,
    TimeProvider? reloj = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<DescargarParteHandler>.Instance;
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    private static readonly DescargarParteResultado NoEncontrada = new(DescargarParteEstado.NoEncontrada);

    /// <summary>
    /// Valida, abre el objeto cifrado y audita. Orden: dueño → estado del lote → parte <c>cerrada</c> → apertura del
    /// objeto (sin leer ningún byte) → <c>parte_descargada</c>. Así la fila de auditoría solo se escribe cuando hay algo
    /// que entregar, y siempre antes del primer byte. Sin <see cref="DescargarParteEstado.Lista"/> no queda nada abierto.
    /// </summary>
    public async Task<DescargarParteResultado> PrepararAsync(DescargarParteQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Numero < 1 || query.Numero > short.MaxValue)
            return NoEncontrada;

        // I1: el dueño es el sub. Otro usuario (incluido el Super Admin) no ve el lote: 404, sin auditoría.
        var lote = await lectura.ObtenerDelDuenoAsync(query.LoteId, query.UsuarioId, ct).ConfigureAwait(false);
        if (lote is null)
            return NoEncontrada;

        var ahora = _reloj.GetUtcNow();
        switch (ConsolidadoLoteDescargabilidad.Evaluar(lote, ahora))
        {
            case ConsolidadoLoteDescargable.NoTerminado:
                return new DescargarParteResultado(DescargarParteEstado.NoTerminado);
            case ConsolidadoLoteDescargable.Expirado:
                return new DescargarParteResultado(DescargarParteEstado.Expirada);
            case ConsolidadoLoteDescargable.SinPartes:
                return NoEncontrada;
        }

        var partes = await lectura.ObtenerPartesAsync(lote.Id, ct).ConfigureAwait(false);
        var parte = partes.FirstOrDefault(p => p.PartNumber == query.Numero);
        if (parte is null)
            return NoEncontrada;
        if (parte.Status == ConsolidadoExportPartStatus.Purgada)
            return new DescargarParteResultado(DescargarParteEstado.Expirada);
        if (parte.Status != ConsolidadoExportPartStatus.Cerrada
            || string.IsNullOrWhiteSpace(parte.StoragePath)
            || parte.PlainSizeBytes is not { } bytesEnClaro)
            return NoEncontrada;

        Stream? cifrado;
        try
        {
            cifrado = await almacenamiento.OpenReadAsync(parte.StoragePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAlmacenamientoFallido(_logger, lote.Id, parte.PartNumber, ex.GetType().Name);
            return new DescargarParteResultado(DescargarParteEstado.ParteNoDisponible);
        }

        if (cifrado is null)
        {
            LogObjetoAusente(_logger, lote.Id, parte.PartNumber);
            return new DescargarParteResultado(DescargarParteEstado.ParteNoDisponible);
        }

        // CF-22: parte_descargada síncrona y ANTES del primer byte. Si no se registra, 503 sin bytes.
        bool auditado;
        try
        {
            auditado = await lectura.RegistrarDescargaAsync(
                new ParteDescargadaRegistro(lote, parte.PartNumber, query.RolCodigo, ahora, query.ClientIp, query.UserAgent),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditoriaLanzo(_logger, lote.Id, parte.PartNumber, ex.GetType().Name);
            auditado = false;
        }

        if (!auditado)
        {
            await cifrado.DisposeAsync().ConfigureAwait(false);
            return new DescargarParteResultado(DescargarParteEstado.AuditoriaNoRegistrada);
        }

        var total = Math.Max(lote.PartsCount, parte.PartNumber);
        var nombre = ConsolidadoLoteNombres.Zip(lote.CreatedAt, parte.PartNumber, total);
        return new DescargarParteResultado(
            DescargarParteEstado.Lista, new DescargaParte(lote, parte, nombre, bytesEnClaro, cifrado));
    }

    /// <summary>
    /// Descifra la parte en streaming hacia <paramref name="destino"/>. Solo escribe bloques autenticados; un fallo de
    /// descifrado lanza <see cref="ConsolidadoLoteCifradoException"/>. Si el total escrito no coincide con
    /// <c>plain_size_bytes</c> también lanza (<see cref="ConsolidadoLoteCifradoError.ParteCorrupta"/>): el
    /// <c>Content-Length</c> ya anunciado no se cumpliría.
    /// </summary>
    public async Task<long> EscribirAsync(DescargaParte descarga, Stream destino, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descarga);
        ArgumentNullException.ThrowIfNull(destino);
        var escritos = await cifrador.DescifrarAsync(
            descarga.Lote.DekWrapped, descarga.Lote.Id, descarga.Parte.PartNumber, descarga.Cifrado, destino, ct)
            .ConfigureAwait(false);
        if (escritos != descarga.BytesEnClaro)
        {
            LogLongitudDistinta(_logger, descarga.Lote.Id, descarga.Parte.PartNumber, escritos, descarga.BytesEnClaro);
            throw new ConsolidadoLoteCifradoException(ConsolidadoLoteCifradoError.ParteCorrupta);
        }

        return escritos;
    }

    // Logs sin PII: ids, número de parte, tipos y tamaños.
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: no se pudo abrir la parte {PartNumber} en el almacenamiento ({Tipo}).")]
    private static partial void LogAlmacenamientoFallido(ILogger logger, Guid loteId, short partNumber, string tipo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: la parte {PartNumber} está cerrada pero su objeto no existe en el almacenamiento.")]
    private static partial void LogObjetoAusente(ILogger logger, Guid loteId, short partNumber);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: el registro de parte_descargada de la parte {PartNumber} lanzó {Tipo}; no se entregan bytes.")]
    private static partial void LogAuditoriaLanzo(ILogger logger, Guid loteId, short partNumber, string tipo);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: la parte {PartNumber} descifró {Escritos} bytes y se esperaban {Esperados}.")]
    private static partial void LogLongitudDistinta(ILogger logger, Guid loteId, short partNumber, long escritos, long esperados);
}
