using System.Net;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Aviso a quien radicó de que el organismo anuló el pago en caja de su trámite (HU #14461). Complementa el correo de cierre
/// (<see cref="ICorreoDeCierre"/>): solo se envía si esa persona ya había recibido «Tu trámite quedó pagado».
/// </summary>
public interface ICorreoDeAnulacion
{
    /// <returns><c>true</c> si en esta llamada se envió el correo.</returns>
    Task<bool> EnviarSiCorrespondeAsync(Guid cobroId, CancellationToken ct);
}

/// <summary>
/// Garantías: (1) solo se avisa si el cobro anulado tenía el correo de cierre enviado (<c>closure_email_sent_at</c>): si nunca se
/// le dijo «quedó pagado» no hay nada que corregir; (2) solo al correo propio de quien radicó, nunca al técnico del punto ni a
/// otras partes; (3) una sola vez: la anulación ocurre una sola vez por cobro (una segunda anulación se rechaza como no anulable) y
/// este envío solo lo invoca la anulación que ganó; (4) nunca lanza ni deshace la anulación; (5) el cuerpo lleva solo el nombre
/// de quien radicó, el código público y el organismo: sin el motivo interno ni datos de las partes; el correo no se escribe en logs.
/// </summary>
public sealed partial class CorreoDeAnulacion(
    PersonaNaturalDb db,
    PersonaService personas,
    IEnviadorCotizacion enviador,
    IConfiguration configuracion,
    ILogger<CorreoDeAnulacion> logger) : ICorreoDeAnulacion
{
    public const string Plantilla = "persona-natural.pago-anulado-punto";
    public const string Asunto = "Tu pago fue anulado";

    public async Task<bool> EnviarSiCorrespondeAsync(Guid cobroId, CancellationToken ct)
    {
        try
        {
            return await IntentarAsync(cobroId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Solo el tipo: el mensaje podría traer el destinatario.
            Fallo(logger, ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> IntentarAsync(Guid cobroId, CancellationToken ct)
    {
        var cobro = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == cobroId, ct).ConfigureAwait(false);
        // La persona solo recibió «quedó pagado» si el correo de cierre salió.
        if (cobro is null || cobro.Estado != EstadoCobro.Anulado || cobro.CorreoCierreEnviadoEn is null)
            return false;

        var s = await db.Set<SolicitudTramite>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == cobro.SolicitudId, ct).ConfigureAwait(false);
        if (s is null || !SolicitudesService.EsCanalPunto(s.Datos))
            return false;

        var rol = RolesPunto.Desde(s.DefinicionSnapshot).FirstOrDefault(r => r.EsRadicante)?.Rol;
        if (rol is null)
            return false;
        var radicante = (await personas.LeerAsync(s.Id, "anulacion_correo", ct).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Parte, rol, StringComparison.OrdinalIgnoreCase) && p.Ordinal == 1);
        var destino = CorreoDeCierre.CorreoPropio(configuracion, radicante?.Correo, s.Id);
        if (destino is null)
            return false;

        return await enviador.EnviarAsync(destino, Asunto, ArmarHtml(radicante?.Nombre, s.CodigoPublico, s.OrganismoNombre), Plantilla, ct).ConfigureAwait(false);
    }

    internal static string ArmarHtml(string? nombre, string codigoPublico, string organismo)
    {
        var saludo = string.IsNullOrWhiteSpace(nombre) ? "Hola" : "Hola " + WebUtility.HtmlEncode(nombre.Trim());
        return "<div style=\"font-family:Arial,sans-serif;max-width:560px\"><h2>Tu pago fue anulado</h2>"
            + $"<p>{saludo},</p>"
            + $"<p>El pago en caja de tu trámite <b>{WebUtility.HtmlEncode(codigoPublico)}</b> fue anulado por el organismo.</p>"
            + $"<p>Acércate a la caja de <b>{WebUtility.HtmlEncode(organismo)}</b> para generar el pago de nuevo.</p>"
            + "<p style=\"color:#555\">Guarda este código: con él puedes consultar el estado de tu trámite.</p></div>";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo enviar el correo de anulación del pago ({Tipo}); la anulación sigue firme")]
    private static partial void Fallo(ILogger logger, string tipo);
}
