using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>
/// Aviso «lote terminado» (HU #14694, M12; F3-CF7, plan §5.9). Solo para lotes <c>MASIVA</c> y una sola vez por lote (marca
/// <c>aviso_enviado_en</c>), en el contexto y la transacción de quien terminó el lote:
/// <list type="bullet">
///   <item>Aviso en pantalla <see cref="TipoNotificacion"/> para la compañía (<c>impuestos.notificaciones</c>).</item>
///   <item>Un trabajo <see cref="TipoTrabajo"/> por la outbox (exchange <c>flit.notificaciones</c>) por cada correo de
///   <c>correos_aviso</c>: plantilla <see cref="Plantilla"/>, canal <see cref="Canal"/>, destinatario <c>{email, nombre = email}</c>
///   (la configuración no guarda nombres) y sin tema (el de FLIT por defecto).</item>
/// </list>
/// Textos cerrados del plan §5.9: el asunto y tres párrafos; <c>{estado}</c> es la etiqueta del contrato y el párrafo del enlace solo
/// va con <see cref="ClaveUrlFrontend"/>. Los valores se escapan como HTML (sin convertir tildes en entidades). Sin placas,
/// propietarios, documentos ni montos. El nombre de la compañía sale del directorio de la plataforma (la misma fuente para el cierre
/// automático y el manual); si no responde, el que dio quien llama (el del token), y sin ninguno la frase omite «de {empresa}».
/// </summary>
/// <example>
/// <code>
/// await aviso.EmitirAsync(lote, ahora, "Compañía Demo", ct); // sin guardar: lo guarda la transacción del lote
/// </code>
/// </example>
public sealed partial class AvisoLoteTerminado(
    ImpuestosDb db, IPlatformOutbox outbox, AmbitoDeLaEmpresa ambito, IConfiguration configuracion, ILogger<AvisoLoteTerminado> log)
{
    public const string TipoTrabajo = "notificaciones.email.send";
    public const string Plantilla = "impuestos.lote.terminado";
    public const string Canal = "flit_smtp";
    public const string TipoNotificacion = "LOTE_TERMINADO";
    public const string ClaveUrlFrontend = "Impuestos:Avisos:UrlFrontend";

    /// <summary>Etiquetas del contrato (<c>EstadoLiquidacion.x-flit-etiquetas</c>) de los estados terminales.</summary>
    public static IReadOnlyDictionary<EstadoLote, string> Etiquetas { get; } = new Dictionary<EstadoLote, string>
    {
        [EstadoLote.Completado] = "Completado",
        [EstadoLote.CompletadoConNovedades] = "Completado con novedades",
        [EstadoLote.CerradoManualmente] = "Cerrado manualmente",
    };

    private static readonly HtmlEncoder Codificador = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>Asunto del plan §5.9.</summary>
    public static string Asunto(string nombreArchivo) => $"Impuestos: terminó la liquidación masiva «{nombreArchivo}»";

    /// <summary>
    /// Cuerpo del plan §5.9: párrafos de texto plano con los valores escapados. Con resultado = exitosas; con novedad = error, datos
    /// incompletos, departamento no habilitado e inciertas; sin resultado = lote cerrado.
    /// </summary>
    public static string Html(Liquidacion lote, string? nombreEmpresa, string? urlFrontend)
    {
        ArgumentNullException.ThrowIfNull(lote);
        static string E(string valor) => Codificador.Encode(valor);
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        var archivo = E(lote.NombreArchivo ?? string.Empty);
        var empresa = string.IsNullOrWhiteSpace(nombreEmpresa) ? string.Empty : $" de {E(nombreEmpresa.Trim())}";
        var estado = E(Etiquetas.GetValueOrDefault(lote.Estado, string.Empty));
        var conNovedad = lote.CError + lote.CDatosIncompletos + lote.CDptoNoHabilitado + lote.CIncierto;
        var html = $"<p>La liquidación masiva «{archivo}»{empresa} terminó: {estado}.</p>" +
            $"<p>Placas: {N(lote.TotalItems)}. Con resultado: {N(lote.CExitosas)}. Con novedad: {N(conNovedad)}. Sin resultado: {N(lote.CSinResultado)}.</p>";
        if (!string.IsNullOrWhiteSpace(urlFrontend))
            html += $"<p>Consulta el detalle en Impuestos: {E($"{urlFrontend.Trim().TrimEnd('/')}/liquidaciones/{lote.Id}")}.</p>";
        return html;
    }

    /// <summary>Emite el aviso del lote (si es MASIVA) y marca <c>aviso_enviado_en</c>; no guarda.</summary>
    public async Task EmitirAsync(Liquidacion lote, DateTimeOffset ahora, string? nombreEmpresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (lote.Tipo != TipoLiquidacion.Masiva || lote.AvisoEnviadoEn is not null)
            return;

        var correos = await db.Set<ConfiguracionEmpresa>().AsNoTracking().Where(c => c.TenantId == lote.TenantId)
            .Select(c => c.CorreosAviso).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? [];
        db.Add(new Notificacion { TenantId = lote.TenantId, LiquidacionId = lote.Id, Tipo = TipoNotificacion, CreadaEn = ahora });
        if (correos.Length > 0)
        {
            var directorio = (await ambito.IdentificarAsync(lote.TenantId, ct).ConfigureAwait(false)).Nombre;
            var nombre = string.IsNullOrWhiteSpace(directorio) ? nombreEmpresa : directorio;
            var asunto = Asunto(lote.NombreArchivo ?? string.Empty);
            var html = Html(lote, nombre, configuracion[ClaveUrlFrontend]);
            foreach (var correo in correos.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                outbox.EnqueueJob(TipoTrabajo, 1, lote.TenantId, new
                {
                    plantilla = Plantilla,
                    canal = Canal,
                    destinatario = new { email = correo, nombre = correo },
                    asunto,
                    html,
                });
            }
        }

        lote.AvisoEnviadoEn = ahora;
        Emitido(log, lote.Id, lote.Estado, correos.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso de lote terminado del lote {Lote} ({Estado}): {Correos} correos encolados.")]
    private static partial void Emitido(ILogger logger, Guid lote, EstadoLote estado, int correos);
}
