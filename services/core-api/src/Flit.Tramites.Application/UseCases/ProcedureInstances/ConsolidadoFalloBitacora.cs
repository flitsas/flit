using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12798 (Épica #12760) — bitácora de los fallos de regeneración del consolidado y productor de
/// <see cref="GenerarConsolidadoResult.AvisosCascada"/> para ese fallo.
///
/// <para><b>Mecanismo reutilizado.</b> El evento va a <c>tramites.procedure_instance_events</c> (la misma
/// bitácora donde <see cref="GenerarConsolidadoHandler"/> deja <c>consolidado_generado</c>) a través del
/// puerto <see cref="IRegeneracionDocumentalTrazaWriter"/> del Bug #11613: SQL parametrizado fuera del
/// change tracker. Es obligatorio aquí: el fallo llega con el tracker arrastrando el intento a medio
/// hacer (fila nueva añadida, previas retiradas) y un <c>SaveChanges</c> lo volcaría a la base.</para>
///
/// <para><b>Qué es fallo (AC5).</b> Cualquier código de error del generador y cualquier excepción, salvo
/// las omisiones legítimas: <c>not_found</c> y <c>migrado_solo_lectura</c>. Las demás omisiones
/// (estado final, <c>Source="user"</c>, migrado, vigente) se deciden ANTES de invocar al generador en
/// <see cref="EntregarConsolidadoHandler"/> y <see cref="RegenerarConsolidadoAnticipadoHandler"/>, así que
/// nunca llegan aquí.</para>
///
/// <para><b>Sin datos sensibles (AC6).</b> El payload lleva código de causa, tipo de excepción y un
/// mensaje saneado (<see cref="Sanear"/>); nunca <c>ex.ToString()</c>, trazas, URLs ni rutas.</para>
///
/// <para>Uso de ejemplo:
/// <code>
/// var salida = await bitacora.GenerarConRespaldoAsync(
///     tenantId, id, "consolidado", ConsolidadoFalloBitacora.Origenes.EntregaConsolidado, anterior,
///     c => wizardHandler.HandleAsync(id, tenantId, userId, force, c), ct);
/// // salida.SirvioAnterior ⇒ salida.Result.AvisosCascada == ["consolidado: storage_unavailable"]
/// </code></para>
/// </summary>
public sealed partial class ConsolidadoFalloBitacora(
    IRegeneracionDocumentalTrazaWriter? trazaWriter = null,
    ILogger<ConsolidadoFalloBitacora>? logger = null)
{
    /// <summary>Tipo del evento persistido en <c>tramites.procedure_instance_events</c>.</summary>
    public const string EventoFallo = "consolidado_regeneracion_fallida";

    /// <summary>Código de causa cuando el generador lanzó una excepción en vez de devolver un código.</summary>
    public const string CausaExcepcion = "excepcion";

    /// <summary>Longitud máxima del mensaje saneado que se persiste.</summary>
    public const int MensajeMaximo = 160;

    /// <summary>Desde qué flujo se intentó la regeneración (campo <c>origen</c> del payload).</summary>
    public static class Origenes
    {
        /// <summary>Ruta única de entrega (<see cref="EntregarConsolidadoHandler"/>, HU #12785).</summary>
        public const string EntregaConsolidado = "entrega_consolidado";

        /// <summary>POST de generación del wizard (<c>/instances/{id}/consolidado</c>).</summary>
        public const string GeneracionWizard = "generacion_wizard";

        /// <summary>Cola de regeneración anticipada (HU #12795).</summary>
        public const string RegeneracionAnticipada = "regeneracion_anticipada";
    }

    /// <summary>Errores del generador que NO son un fallo de regeneración (AC5: sin ruido).</summary>
    private static readonly HashSet<string> Omisiones = new(StringComparer.Ordinal)
    {
        "not_found",
        "migrado_solo_lectura",
    };

    private readonly IRegeneracionDocumentalTrazaWriter _traza =
        trazaWriter ?? NullRegeneracionDocumentalTrazaWriter.Instance;

    /// <summary>¿El código devuelto por el generador es un fallo que se registra? (<c>null</c> ⇒ no).</summary>
    public static bool EsFallo(string? error) => error is not null && !Omisiones.Contains(error);

    /// <summary>
    /// AC2 — aviso en cascada del fallo, en el formato que ya consume el frontend
    /// (<c>"documento: motivo"</c>, HU #11050): p. ej. <c>"consolidado: storage_unavailable"</c>.
    /// </summary>
    public static string Aviso(string documento, string causa) => $"{documento}: {causa}";

    /// <summary>
    /// Ejecuta <paramref name="generar"/> y, si falla, registra el fallo en la bitácora. Con
    /// <paramref name="anterior"/> disponible (HU #12797: el reemplazo seguro lo deja intacto) devuelve ESE
    /// PDF con el aviso en <see cref="GenerarConsolidadoResult.AvisosCascada"/> en lugar del error (AC4);
    /// sin anterior, el error viaja como siempre (código de error o la excepción relanzada).
    /// </summary>
    public async Task<ConsolidadoConRespaldo> GenerarConRespaldoAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string documento,
        string origen,
        ProcedureInstanceAttachment? anterior,
        Func<CancellationToken, Task<(GenerarConsolidadoResult? Result, string? Error)>> generar,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(generar);

        (GenerarConsolidadoResult? Result, string? Error) salida;
        try
        {
            salida = await generar(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await RegistrarAsync(
                tenantId, procedureInstanceId, origen, documento, CausaExcepcion, ex, anterior is not null, ct)
                .ConfigureAwait(false);
            if (anterior is null)
                throw;

            return new ConsolidadoConRespaldo(ServirAnterior(anterior, documento, CausaExcepcion), null, SirvioAnterior: true);
        }

        if (!EsFallo(salida.Error))
            return new ConsolidadoConRespaldo(salida.Result, salida.Error, SirvioAnterior: false);

        var causa = salida.Error!;
        await RegistrarAsync(
            tenantId, procedureInstanceId, origen, documento, causa, excepcion: null, anterior is not null, ct)
            .ConfigureAwait(false);

        return anterior is null
            ? new ConsolidadoConRespaldo(null, causa, SirvioAnterior: false)
            : new ConsolidadoConRespaldo(ServirAnterior(anterior, documento, causa), null, SirvioAnterior: true);
    }

    /// <summary>
    /// AC1 — escribe el evento <see cref="EventoFallo"/> con causa, documento e instante. Best-effort: si
    /// la propia escritura falla se registra en el log y se sigue (la bitácora no puede tumbar la entrega
    /// ni el trabajo en segundo plano). Devuelve <c>true</c> si la fila quedó escrita.
    /// </summary>
    public async Task<bool> RegistrarAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string origen,
        string documento,
        string causa,
        Exception? excepcion,
        bool conAnterior,
        CancellationToken ct = default)
    {
        if (logger is not null)
            LogFallo(logger, documento, procedureInstanceId, tenantId, origen, causa, excepcion?.GetType().Name, conAnterior);

        var payload = Payload(origen, documento, causa, excepcion, tenantId, DateTimeOffset.UtcNow);
        try
        {
            return await _traza
                .EscribirEventoAsync(tenantId, procedureInstanceId, EventoFallo, payload, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (logger is not null)
                LogBitacoraNoPersistida(logger, ex, procedureInstanceId, documento);
            return false;
        }
    }

    /// <summary>
    /// Payload persistido: <c>origen</c>, <c>documento</c>, <c>error</c> (código de causa),
    /// <c>detalle</c> (tipo de la excepción o null), <c>mensaje</c> (saneado o null),
    /// <c>fallido_at</c> (UTC) y <c>tenant_id</c> — mismas claves que la traza del Bug #11613 más las nuevas.
    /// </summary>
    internal static string Payload(
        string origen, string documento, string causa, Exception? excepcion, Guid tenantId, DateTimeOffset instante) =>
        JsonSerializer.Serialize(new
        {
            origen,
            documento,
            error = causa,
            detalle = excepcion?.GetType().Name,
            mensaje = Sanear(excepcion?.Message),
            fallido_at = instante,
            tenant_id = tenantId,
        });

    /// <summary>
    /// AC6 — mensaje de excepción apto para persistir: sin URLs (presignadas o con credenciales), correos,
    /// rutas de disco, valores entre comillas o paréntesis (donde el proveedor/BD suele citar el dato) ni
    /// secuencias numéricas de 4+ dígitos (documentos, teléfonos); una sola línea y acotado.
    /// </summary>
    public static string? Sanear(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return null;

        var s = UrlRegex().Replace(mensaje, "[url]");
        s = EmailRegex().Replace(s, "[email]");
        s = RutaWindowsRegex().Replace(s, "[ruta]");
        s = RutaUnixRegex().Replace(s, "$1[ruta]");
        s = ComillasRegex().Replace(s, "'***'");
        s = ParentesisRegex().Replace(s, "(***)");
        s = DigitosRegex().Replace(s, "#");
        s = EspaciosRegex().Replace(s, " ").Trim();
        return s.Length <= MensajeMaximo ? s : s[..MensajeMaximo];
    }

    private static GenerarConsolidadoResult ServirAnterior(
        ProcedureInstanceAttachment anterior, string documento, string causa) =>
        new(
            new ConsolidadoDocumentDto(anterior.Id, anterior.Tipo, anterior.Filename, anterior.Sha256),
            Regenerado: false,
            AvisosCascada: [Aviso(documento, causa)]);

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://\S+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"[^\s@'""()]+@[^\s@'""()]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[A-Za-z]:\\\S*")]
    private static partial Regex RutaWindowsRegex();

    [GeneratedRegex(@"(^|\s)/\S+")]
    private static partial Regex RutaUnixRegex();

    [GeneratedRegex(@"'[^']*'|""[^""]*""")]
    private static partial Regex ComillasRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParentesisRegex();

    [GeneratedRegex(@"\d{4,}")]
    private static partial Regex DigitosRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRegex();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Regeneración del consolidado {Documento} fallida (instancia {InstanceId}, tenant {TenantId}, origen {Origen}): {Causa} [{TipoExcepcion}]. Se entrega el anterior: {ConAnterior}.")]
    private static partial void LogFallo(
        ILogger logger, string documento, Guid instanceId, Guid tenantId, string origen, string causa,
        string? tipoExcepcion, bool conAnterior);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No se pudo persistir en la bitácora el fallo de regeneración del consolidado {Documento} (instancia {InstanceId}); queda solo en los logs.")]
    private static partial void LogBitacoraNoPersistida(ILogger logger, Exception ex, Guid instanceId, string documento);
}

/// <summary>
/// HU #12798 — salida de <see cref="ConsolidadoFalloBitacora.GenerarConRespaldoAsync"/>.
/// <paramref name="SirvioAnterior"/> = la regeneración falló y <paramref name="Result"/> es el PDF
/// anterior con el aviso en <see cref="GenerarConsolidadoResult.AvisosCascada"/>.
/// </summary>
public sealed record ConsolidadoConRespaldo(GenerarConsolidadoResult? Result, string? Error, bool SirvioAnterior);

/// <summary>
/// HU #12798 (AC4) — POST de generación del consolidado del wizard con respaldo: si la reconstrucción
/// falla y existe un consolidado anterior, se entrega ese PDF con el aviso de que la actualización
/// falló (en <see cref="GenerarConsolidadoResult.AvisosCascada"/>) en vez de un error sin documento. Sin
/// anterior, el comportamiento es el de siempre. La generación sigue siendo la de
/// <see cref="GenerarConsolidadoHandler"/>: aquí solo se captura el anterior y se envuelve la llamada.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await handler.HandleAsync(id, tenantId, userId, force: true, ct)</c>.</remarks>
public sealed class GenerarConsolidadoConRespaldoHandler(
    IProcedureInstanceRepository repo,
    GenerarConsolidadoHandler generar,
    ConsolidadoFalloBitacora? bitacora = null)
{
    private const string TipoAdjunto = "consolidado";

    private readonly ConsolidadoFalloBitacora _bitacora = bitacora ?? new ConsolidadoFalloBitacora();

    public async Task<(GenerarConsolidadoResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        Guid? userId,
        bool force,
        CancellationToken ct = default)
    {
        // El anterior se captura ANTES de generar: si la generación falla, el reemplazo seguro
        // (HU #12797) garantiza que su fila y su binario siguen en pie.
        var instance = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        var anterior = instance is null ? null : ConsolidadoEntregaModos.Existente(instance, TipoAdjunto);

        var salida = await _bitacora
            .GenerarConRespaldoAsync(
                tenantId, id, TipoAdjunto, ConsolidadoFalloBitacora.Origenes.GeneracionWizard, anterior,
                c => generar.HandleAsync(id, tenantId, userId, force, c), ct)
            .ConfigureAwait(false);

        return (salida.Result, salida.Error);
    }
}
