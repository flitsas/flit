using System.Text.Json;
using Flit.Impuestos.Api.Infraestructura.Archivos;
using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Impuestos.Api.Modulos.Recibos;

/// <summary>Qué cambió en una pasada de <see cref="DescargaRecibos.DescargarAsync"/> (solo lo de esa pasada).</summary>
/// <param name="Guardados">Recibos que quedaron <c>GUARDADO</c>.</param>
/// <param name="SinArchivo">Recibos que quedaron <c>SIN_ARCHIVO</c> (<c>404 file_missing</c> o <c>not_found</c>).</param>
/// <param name="PorReintentar">Recibos que siguen <c>PENDIENTE</c> tras una falla transitoria: el consumidor pide reintento.</param>
/// <param name="Manuales">Recibos que agotaron los intentos y quedaron <c>PENDIENTE_REINTENTO_MANUAL</c>.</param>
/// <param name="SinCredencial">La compañía no tiene api_key, o el proveedor la rechazó (401): no se descargó nada.</param>
public sealed record ResultadoDescarga(int Guardados, int SinArchivo, int PorReintentar, int Manuales, bool SinCredencial)
{
    public static ResultadoDescarga Nada { get; } = new(0, 0, 0, 0, false);
}

/// <summary>
/// Descarga los recibos PDF de un ítem con resultado y los guarda en el almacén de la suite (HU #14694 M11; plan §1.6 y §2.4;
/// DA-9c; guía de integración §8):
/// <list type="number">
///   <item>Lee el <c>result</c> cifrado del ítem (solo <c>EXITOSO</c>) y saca los archivos declarados, uno por <c>fileId</c>
///   distinto: <c>pendingPeriods[].fileId</c> (VIGENCIA, con todas las vigencias que comparten el archivo),
///   <c>consolidatedFileId</c> (CONSOLIDADO) y <c>clearance.fileId</c> (PAZ_Y_SALVO). Un <c>fileId</c> null no tiene PDF y no crea
///   recibo. Los recibos de un resultado anterior que ya no están declarados se retiran.</item>
///   <item>Por cada recibo <c>PENDIENTE</c>: <c>GET /api/v1/files/{fileId}/download</c> con el <c>X-API-Key</c> de la compañía dueña.
///   200 ⇒ se guarda en el almacén (categoría <c>impuestos</c>) y queda <c>GUARDADO</c> con referencia, sha256 y tamaño;
///   <c>404 file_missing</c> ⇒ <c>SIN_ARCHIVO</c> con <c>FILE_MISSING</c>; otro 404 ⇒ <c>SIN_ARCHIVO</c> con <c>NOT_FOUND</c>;
///   <c>503</c> ⇒ sigue <c>PENDIENTE</c> con <c>STORAGE_UNAVAILABLE</c>; cualquier otra falla (red, otro 5xx, otro 4xx o el
///   almacén de la suite) también cuenta como intento, sin cambiar el motivo. Al llegar a <see cref="IntentosMaximos"/> intentos
///   queda <c>PENDIENTE_REINTENTO_MANUAL</c>. Las esperas entre intentos (<see cref="Esperas"/>) las pone la cola del SDK.</item>
/// </list>
/// Trabaja con un contexto propio de la compañía y guarda cada recibo al terminarlo: lo hecho y el contador de intentos sobreviven
/// aunque el mensaje se reintente (el SDK revierte su transacción al lanzar). Lo <c>GUARDADO</c>, <c>SIN_ARCHIVO</c> o manual no se
/// vuelve a pedir. Logs: ids, estados y códigos HTTP; nunca la api_key, la placa ni el contenido.
/// </summary>
/// <example>
/// <code>
/// var r = await descarga.DescargarAsync(empresa, itemId, ct);
/// if (r.PorReintentar &gt; 0) throw new RecibosPorReintentarException("…"); // el SDK reintenta a 1, 5 y 30 min
/// </code>
/// </example>
public sealed partial class DescargaRecibos(
    AlmacenCredenciales almacen, ClienteApiLiquidacion api, IAlmacenArchivos archivos, IProteccionImpuestos proteccion, ILogger<DescargaRecibos> log)
{
    /// <summary>El primero y los tres reintentos del plan §1.6.</summary>
    public const int IntentosMaximos = 4;

    /// <summary>Tipo del archivo en el almacén (etiqueta y metadato del file-manager).</summary>
    public const string TipoArchivo = "recibo";

    public const string ContentTypePdf = "application/pdf";

    public const string MotivoFileMissing = "FILE_MISSING";
    public const string MotivoNotFound = "NOT_FOUND";
    public const string MotivoStorageUnavailable = "STORAGE_UNAVAILABLE";

    private const int LargoFileId = 64;
    private const int LargoPeriodo = 9;

    /// <summary>Esperas antes de cada reintento automático (plan §1.6): 1, 5 y 30 minutos.</summary>
    public static IReadOnlyList<TimeSpan> Esperas { get; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    private enum Paso
    {
        Guardado,
        SinArchivo,
        PorReintentar,
        Manual,
        CredencialRechazada,
    }

    public async Task<ResultadoDescarga> DescargarAsync(Guid empresa, Guid itemId, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var item = await db.Set<LiquidacionItem>().AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.Estado, i.ResultadoCifrado }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
        {
            SinItem(log, itemId);
            return ResultadoDescarga.Nada;
        }

        IReadOnlyList<ArchivoDeclarado>? declarados = item.ResultadoCifrado is { } cifrado
            ? item.Estado == EstadoItem.Exitoso ? Declarados(proteccion.DesprotegerResultado(cifrado)) : []
            : null;
        var recibos = await SincronizarAsync(db, empresa, itemId, declarados, ct).ConfigureAwait(false);
        var pendientes = recibos.Where(r => r.Estado == EstadoRecibo.Pendiente).OrderBy(r => r.Id).ToList();
        if (pendientes.Count == 0)
            return ResultadoDescarga.Nada;

        var credencial = await almacen.LeerAsync(db, empresa, ct).ConfigureAwait(false);
        if (credencial is null)
        {
            SinCredencialLog(log, itemId, pendientes.Count);
            return ResultadoDescarga.Nada with { SinCredencial = true };
        }

        int guardados = 0, sinArchivo = 0, porReintentar = 0, manuales = 0;
        foreach (var recibo in pendientes)
        {
            var paso = await DescargarUnoAsync(empresa, recibo, credencial.ApiKey, ct).ConfigureAwait(false);
            if (paso == Paso.CredencialRechazada)
            {
                CredencialRechazadaLog(log, itemId);
                return new ResultadoDescarga(guardados, sinArchivo, porReintentar, manuales, SinCredencial: true);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            Resultado(log, recibo.Id, itemId, recibo.Estado, recibo.Intentos);
            switch (paso)
            {
                case Paso.Guardado: guardados++; break;
                case Paso.SinArchivo: sinArchivo++; break;
                case Paso.PorReintentar: porReintentar++; break;
                case Paso.Manual: manuales++; break;
            }
        }

        return new ResultadoDescarga(guardados, sinArchivo, porReintentar, manuales, SinCredencial: false);
    }

    private async Task<Paso> DescargarUnoAsync(Guid empresa, Recibo recibo, string apiKey, CancellationToken ct)
    {
        var respuesta = await api.DescargarArchivoAsync(apiKey, recibo.FileIdApi, ct).ConfigureAwait(false);
        if (respuesta is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status401Unauthorized })
            return Paso.CredencialRechazada;

        recibo.Intentos++;
        if (respuesta.Exitoso && respuesta.Valor!.Contenido.Length > 0)
        {
            try
            {
                var guardado = await archivos.GuardarAsync(empresa, TipoArchivo, $"recibo-{recibo.Id:N}.pdf", ContentTypePdf, respuesta.Valor.Contenido, ct)
                    .ConfigureAwait(false);
                recibo.Estado = EstadoRecibo.Guardado;
                recibo.ReferenciaAlmacen = guardado.Referencia;
                recibo.Sha256 = guardado.Sha256;
                recibo.Tamano = guardado.Tamano;
                recibo.Motivo = null;
                return Paso.Guardado;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                AlmacenFallo(log, recibo.Id, ex.GetType().Name);
                return Transitorio(recibo, recibo.Motivo);
            }
        }

        if (respuesta is { Clase: ClaseEnvio.Respuesta, Status: StatusCodes.Status404NotFound })
        {
            recibo.Estado = EstadoRecibo.SinArchivo;
            recibo.Motivo = string.Equals(respuesta.CodigoError, ErroresApi.FileMissing, StringComparison.Ordinal) ? MotivoFileMissing : MotivoNotFound;
            return Paso.SinArchivo;
        }

        return Transitorio(recibo, respuesta.Status == StatusCodes.Status503ServiceUnavailable ? MotivoStorageUnavailable : recibo.Motivo);
    }

    private static Paso Transitorio(Recibo recibo, string? motivo)
    {
        recibo.Motivo = motivo;
        if (recibo.Intentos < IntentosMaximos)
            return Paso.PorReintentar;
        recibo.Estado = EstadoRecibo.PendienteReintentoManual;
        return Paso.Manual;
    }

    /// <summary>
    /// Deja en la base un recibo por archivo declarado (los nuevos <c>PENDIENTE</c>; tipo y vigencias al día) y retira los que ya no
    /// están declarados. Sin resultado (<paramref name="declarados"/> null) no toca nada. Devuelve los recibos del ítem con seguimiento.
    /// </summary>
    private static async Task<List<Recibo>> SincronizarAsync(ImpuestosDb db, Guid empresa, Guid itemId, IReadOnlyList<ArchivoDeclarado>? declarados, CancellationToken ct)
    {
        for (var vuelta = 0; ; vuelta++)
        {
            var recibos = await db.Set<Recibo>().Where(r => r.ItemId == itemId).ToListAsync(ct).ConfigureAwait(false);
            if (declarados is null)
                return recibos;

            var porArchivo = recibos.ToDictionary(r => r.FileIdApi, StringComparer.Ordinal);
            foreach (var d in declarados)
            {
                if (porArchivo.Remove(d.FileId, out var existente))
                {
                    existente.Tipo = d.Tipo;
                    if (!existente.Periodos.SequenceEqual(d.Periodos, StringComparer.Ordinal))
                        existente.Periodos = [.. d.Periodos];
                    continue;
                }

                var nuevo = new Recibo { ItemId = itemId, TenantId = empresa, FileIdApi = d.FileId, Tipo = d.Tipo, Periodos = [.. d.Periodos] };
                db.Add(nuevo);
                recibos.Add(nuevo);
            }

            foreach (var retirado in porArchivo.Values)
            {
                db.Remove(retirado);
                recibos.Remove(retirado);
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return recibos;
            }
            catch (DbUpdateException ex) when (vuelta == 0 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Otra entrega del mismo ítem insertó los recibos a la vez: se releen y se sigue con los de la base.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Archivo declarado en el <c>result</c>: un <c>fileId</c> con su tipo y las vigencias que cubre.</summary>
    internal sealed record ArchivoDeclarado(string FileId, TipoRecibo Tipo, IReadOnlyList<string> Periodos);

    /// <summary>
    /// Los archivos de una consulta (guía §8), en orden de aparición y sin repetir <c>fileId</c>: un <c>fileId</c> que se repite entre
    /// vigencias suma sus vigencias; el consolidado o el paz y salvo con un <c>fileId</c> ya visto no crea otro recibo. Un
    /// <c>fileId</c> null, vacío o más largo que la columna no cuenta.
    /// </summary>
    internal static IReadOnlyList<ArchivoDeclarado> Declarados(string consultaJson)
    {
        ArgumentNullException.ThrowIfNull(consultaJson);
        var orden = new List<string>();
        var tipos = new Dictionary<string, TipoRecibo>(StringComparer.Ordinal);
        var periodos = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Sumar(string? fileId, TipoRecibo tipo, string? periodo)
        {
            if (string.IsNullOrWhiteSpace(fileId) || fileId.Length > LargoFileId)
                return;
            if (!tipos.ContainsKey(fileId))
            {
                orden.Add(fileId);
                tipos[fileId] = tipo;
                periodos[fileId] = [];
            }

            if (periodo is { Length: > 0 } p)
            {
                var recortado = p.Length <= LargoPeriodo ? p : p[..LargoPeriodo];
                if (!periodos[fileId].Contains(recortado, StringComparer.Ordinal))
                    periodos[fileId].Add(recortado);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(consultaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                return [];
            if (result.TryGetProperty("pendingPeriods", out var lista) && lista.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in lista.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object))
                    Sumar(Texto(p, "fileId"), TipoRecibo.Vigencia, TextoONumero(p, "period"));
            }

            Sumar(Texto(result, "consolidatedFileId"), TipoRecibo.Consolidado, null);
            if (result.TryGetProperty("clearance", out var paz) && paz.ValueKind == JsonValueKind.Object)
                Sumar(Texto(paz, "fileId"), TipoRecibo.PazYSalvo, null);
        }
        catch (JsonException)
        {
            return [];
        }

        return [.. orden.Select(f => new ArchivoDeclarado(f, tipos[f], periodos[f]))];
    }

    private static string? Texto(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? TextoONumero(JsonElement padre, string nombre) =>
        padre.TryGetProperty(nombre, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Recibo {Recibo} del ítem {Item}: {Estado} (intento {Intentos}).")]
    private static partial void Resultado(ILogger logger, Guid recibo, Guid item, EstadoRecibo estado, int intentos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recibos: el ítem {Item} no existe en la compañía del trabajo; sin efecto.")]
    private static partial void SinItem(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recibos del ítem {Item}: la compañía no tiene api_key; {Pendientes} recibo(s) quedan pendientes.")]
    private static partial void SinCredencialLog(ILogger logger, Guid item, int pendientes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recibos del ítem {Item}: el proveedor rechazó la api_key (401); quedan pendientes.")]
    private static partial void CredencialRechazadaLog(ILogger logger, Guid item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recibo {Recibo}: falló el almacén de la suite ({Tipo}); cuenta como intento.")]
    private static partial void AlmacenFallo(ILogger logger, Guid recibo, string tipo);
}
