using System.Net;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>Estados del cierre de un trámite pagado en el punto (HU #14464).</summary>
public static class EstadoDeCierre
{
    /// <summary>Pagado: FLIT 2.0 todavía no respondió con el trámite.</summary>
    public const string EnRegistro = "en_registro";

    /// <summary>FLIT 2.0 creó el trámite: ya hay radicado.</summary>
    public const string Registrado = "registrado";

    /// <summary>FLIT 2.0 no pudo crear el trámite; se informa un motivo legible.</summary>
    public const string Rechazado = "rechazado";
}

/// <summary>Dónde se recoge el trámite: el organismo del punto.</summary>
public sealed record RecogeEnOrganismo(string Organismo);

/// <summary>
/// Cierre del trámite pagado: estado, código público (el que se consulta), radicado de FLIT 2.0 cuando existe, pago y dónde se recoge.
/// Sin datos personales de las partes.
/// </summary>
public sealed record CierrePunto(
    string EstadoCierre,
    string CodigoPublico,
    string? RadicadoFlit2,
    string? Motivo,
    long ValorCop,
    string Medio,
    string? MedioCaja,
    DateTimeOffset PagadoEn,
    RecogeEnOrganismo RecogeEn);

/// <summary>
/// Cierre del paso Pago del punto (Feature CIERRE OT; HU #14464). Una sola llamada devuelve en qué quedó el trámite pagado.
/// Sin pago confirmado responde 409 <c>pago_pendiente</c>.
/// </summary>
public sealed class CierrePuntoService(
    PersonaNaturalDb db, SesionService sesiones, RadicacionDePagadasService radicacion, ICorreoDeCierre correo, BloqueoConsultaCierre bloqueo)
{
    public const string CodigoPagoPendiente = "pago_pendiente";
    public const string CodigoNoEncontrado = "no_encontrado";
    public const string CodigoConsultaBloqueada = "consulta_bloqueada";
    internal const string MotivoGenerico = "El organismo no pudo registrar el trámite. Acércate al organismo con tu código para que lo revisen.";

    /// <summary>Cierre del trámite de la sesión (cookie del punto + token del trámite).</summary>
    public async Task<CierrePunto> DelTramiteAsync(OrganismoPunto punto, string? token, CancellationToken ct)
    {
        var validada = await sesiones.ValidarAsync(token, ct).ConfigureAwait(false);
        if (validada.Estado != EstadoSesion.Valida)
            throw new ReglaDeNegocioException(StatusCodes.Status401Unauthorized, "session_invalid", "Sesión del trámite", "La sesión del trámite no es válida.");

        var id = validada.SolicitudId;
        return await ArmarAsync(punto, await db.Set<SolicitudTramite>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false), true, ct).ConfigureAwait(false);
    }

    /// <summary>Cierre por código público (o radicado de FLIT 2.0) desde «Consultar» del punto: solo del organismo de la cookie.</summary>
    public async Task<CierrePunto> PorCodigoAsync(OrganismoPunto punto, string? codigo, byte[]? ipHash, CancellationToken ct)
    {
        // Mismo 404 para «no existe», «es de otro organismo» y «no tiene pago»: ninguna respuesta revela que el código existe.
        // Cada 404 gasta cupo del freno (cookie + IP); un acierto lo libera.
        try
        {
            var cierre = await BuscarPorCodigoAsync(punto, codigo, ct).ConfigureAwait(false);
            await bloqueo.LiberarAsync(punto.OrganismoId, ipHash, ct).ConfigureAwait(false);
            return cierre;
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo is "solicitud_no_encontrada" or CompletitudRequisitosPuntoService.CodigoOrganismoAjeno or CodigoPagoPendiente)
        {
            await bloqueo.RegistrarFalloAsync(punto.OrganismoId, ipHash, ct).ConfigureAwait(false);
            throw new ReglaDeNegocioException(StatusCodes.Status404NotFound, CodigoNoEncontrado, "No encontrado", "No encontramos ese trámite.");
        }
    }

    /// <summary>Cuánto falta para que el freno de enumeración libere a esta cookie + IP, o <c>null</c> si puede consultar.</summary>
    public Task<TimeSpan?> EsperaDeConsultaAsync(OrganismoPunto punto, byte[]? ipHash, CancellationToken ct) =>
        bloqueo.EsperaAsync(punto.OrganismoId, ipHash, ct);

    private async Task<CierrePunto> BuscarPorCodigoAsync(OrganismoPunto punto, string? codigo, CancellationToken ct)
    {
        var norma = PiiHasher.NormalizarRadicado(codigo ?? string.Empty);
        SolicitudTramite? s = null;
        if (norma.Length is > 0 and <= 60)
        {
#pragma warning disable CA1862 // EF traduce ToUpper() a upper(columna); StringComparison no.
            s = await db.Set<SolicitudTramite>().AsNoTracking()
                .Where(x => x.OrganismoId == punto.OrganismoId && (x.CodigoPublico == norma || (x.ReferenceNumber != null && x.ReferenceNumber.ToUpper() == norma)))
                .OrderBy(x => x.CodigoPublico == norma ? 0 : 1)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
#pragma warning restore CA1862
        }

        return await ArmarAsync(punto, s, false, ct).ConfigureAwait(false);
    }

    private async Task<CierrePunto> ArmarAsync(OrganismoPunto punto, SolicitudTramite? s, bool reintentar, CancellationToken ct)
    {
        if (s is null || !SolicitudesService.EsCanalPunto(s.Datos))
            throw new ReglaDeNegocioException(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontrado", "No encontramos ese trámite.");
        if (s.OrganismoId != punto.OrganismoId)
            throw new ReglaDeNegocioException(StatusCodes.Status403Forbidden, CompletitudRequisitosPuntoService.CodigoOrganismoAjeno, "Organismo", "Este trámite pertenece a otro organismo.");

        var cobro = await db.Set<CobroSolicitud>().AsNoTracking().FirstOrDefaultAsync(c => c.SolicitudId == s.Id && c.Estado == EstadoCobro.Pagado, ct).ConfigureAwait(false);
        if (cobro is null)
            throw new ReglaDeNegocioException(StatusCodes.Status409Conflict, CodigoPagoPendiente, "Pago pendiente", "El trámite aún no tiene un pago confirmado.");

        if (reintentar)
        {
            // Pagada sin radicar (FLIT 2.0 no respondió) y correo que no salió: cada consulta lo reintenta; nada de esto bloquea el cierre.
            if (s.Estado == EstadoSolicitud.Pagada && await radicacion.IntentarRadicarSiVencioAsync(s.Id, ct).ConfigureAwait(false))
                s = await db.Set<SolicitudTramite>().AsNoTracking().FirstAsync(x => x.Id == s.Id, ct).ConfigureAwait(false);
            if (cobro.CorreoCierreEnviadoEn is null)
                await correo.EnviarSiCorrespondeAsync(s.Id, ct).ConfigureAwait(false);
        }

        string estado;
        string? radicado = null;
        string? motivo = null;
        switch (s.Estado)
        {
            case EstadoSolicitud.Creada:
                estado = EstadoDeCierre.Registrado;
                radicado = s.ReferenceNumber;
                break;
            case EstadoSolicitud.RechazadaCreacion:
                estado = EstadoDeCierre.Rechazado;
                var ultimo = await db.Set<HistorialEstado>().AsNoTracking()
                    .Where(h => h.SolicitudId == s.Id && h.Hasta == EstadoSolicitud.RechazadaCreacion)
                    .OrderByDescending(h => h.OcurrioEn).Select(h => h.Motivo).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                motivo = MotivoLegible(ultimo);
                break;
            default:
                estado = EstadoDeCierre.EnRegistro;
                break;
        }

        return new CierrePunto(
            estado, s.CodigoPublico, radicado, motivo, cobro.ValorCop ?? 0, cobro.Medio ?? string.Empty, cobro.MedioCaja,
            cobro.PagadoEn ?? cobro.UpdatedAt, new RecogeEnOrganismo(s.OrganismoNombre));
    }

    /// <summary>Solo se muestra un motivo escrito en lenguaje claro; códigos o textos técnicos se reemplazan por uno genérico.</summary>
    internal static string MotivoLegible(string? motivo)
    {
        var m = motivo?.Trim();
        var legible = !string.IsNullOrEmpty(m) && m.Length <= 240 && m.Contains(' ', StringComparison.Ordinal)
            && m.All(c => !char.IsControl(c)) && !m.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            && !m.Contains(" at ", StringComparison.Ordinal) && !m.Contains("://", StringComparison.Ordinal);
        return legible ? m! : MotivoGenerico;
    }
}

/// <summary>
/// Correo de cierre a quien radicó (HU #14466). Se invoca al quedar el trámite <c>pagada</c> (cualquier medio) y en las consultas
/// del cierre; envía UNA sola vez.
/// </summary>
public interface ICorreoDeCierre
{
    /// <returns><c>true</c> si en esta llamada se envió el correo.</returns>
    Task<bool> EnviarSiCorrespondeAsync(Guid solicitudId, CancellationToken ct);
}

/// <summary>
/// Garantías: (1) solo al correo propio de quien radica (el radicante, primer rol del tipo): ni el correo técnico del punto ni
/// otras partes; sin correo propio no se envía; (2) una sola vez: el envío se reclama con un UPDATE condicionado
/// (<c>closure_email_sent_at IS NULL</c>) antes de enviar, de modo que dos llamadas a la vez no mandan dos correos, y la marca solo
/// se libera si el transporte falla (se reintenta en la próxima consulta del cierre); (3) nunca lanza: un fallo no afecta el pago;
/// (4) el cuerpo solo lleva el nombre de quien radicó, el código público y el organismo; el correo no se escribe en logs.
/// </summary>
public sealed partial class CorreoDeCierre(
    PersonaNaturalDb db,
    PersonaService personas,
    IEnviadorCotizacion enviador,
    IConfiguration configuracion,
    TimeProvider reloj,
    ILogger<CorreoDeCierre> logger) : ICorreoDeCierre
{
    public const string Plantilla = "persona-natural.cierre-punto";
    public const string Asunto = "Tu trámite quedó pagado";

    public async Task<bool> EnviarSiCorrespondeAsync(Guid solicitudId, CancellationToken ct)
    {
        try
        {
            return await IntentarAsync(solicitudId, ct).ConfigureAwait(false);
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

    private async Task<bool> IntentarAsync(Guid solicitudId, CancellationToken ct)
    {
        var cobro = await db.Set<CobroSolicitud>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.SolicitudId == solicitudId && c.Estado == EstadoCobro.Pagado, ct).ConfigureAwait(false);
        if (cobro is null || cobro.CorreoCierreEnviadoEn is not null)
            return false;

        var s = await db.Set<SolicitudTramite>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == solicitudId, ct).ConfigureAwait(false);
        if (s is null || !SolicitudesService.EsCanalPunto(s.Datos))
            return false;

        var rol = RolesPunto.Desde(s.DefinicionSnapshot).FirstOrDefault(r => r.EsRadicante)?.Rol;
        if (rol is null)
            return false;
        var radicante = (await personas.LeerAsync(solicitudId, "cierre_correo", ct).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Parte, rol, StringComparison.OrdinalIgnoreCase) && p.Ordinal == 1);
        var destino = CorreoPropio(radicante?.Correo, solicitudId);
        if (destino is null)
            return false;

        // Reclamo atómico: solo una llamada pasa de NULL a la hora; las demás ven 0 filas y no envían.
        var ahora = reloj.GetUtcNow();
        var reclamado = await db.Set<CobroSolicitud>()
            .Where(c => c.Id == cobro.Id && c.CorreoCierreEnviadoEn == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.CorreoCierreEnviadoEn, ahora), ct).ConfigureAwait(false);
        if (reclamado == 0)
            return false;

        var enviado = false;
        try
        {
            enviado = await enviador.EnviarAsync(destino, Asunto, ArmarHtml(radicante?.Nombre, s.CodigoPublico, s.OrganismoNombre), Plantilla, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!enviado)
            {
                // El transporte no lo aceptó (o falló): se libera la marca para reintentar. CancellationToken.None: debe liberarse aunque se cancele.
                await db.Set<CobroSolicitud>().Where(c => c.Id == cobro.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.CorreoCierreEnviadoEn, (DateTimeOffset?)null), CancellationToken.None).ConfigureAwait(false);
            }
        }

        return enviado;
    }

    /// <summary>El correo digitado por quien radica; nunca el técnico del punto (ni uno inutilizable).</summary>
    internal string? CorreoPropio(string? correo, Guid solicitudId)
    {
        if (!IdentidadService.CorreoUtilizableInSitu(correo))
            return null;
        var limpio = correo!.Trim();
        if (string.Equals(limpio, CorreoTecnicoPunto.Para(configuracion, solicitudId), StringComparison.OrdinalIgnoreCase))
            return null;
        // Si la plantilla técnica cambió después de guardar la persona, el prefijo por defecto sigue siendo técnico.
        if (limpio.StartsWith("validaciones+punto-", StringComparison.OrdinalIgnoreCase) && limpio.EndsWith("@flitsas.com", StringComparison.OrdinalIgnoreCase))
            return null;
        return limpio;
    }

    internal static string ArmarHtml(string? nombre, string codigoPublico, string organismo)
    {
        var saludo = string.IsNullOrWhiteSpace(nombre) ? "Hola" : "Hola " + WebUtility.HtmlEncode(nombre.Trim());
        return "<div style=\"font-family:Arial,sans-serif;max-width:560px\"><h2>Tu trámite quedó pagado</h2>"
            + $"<p>{saludo},</p>"
            + $"<p>Recibimos el pago de tu trámite. Tu código para consultarlo es <b>{WebUtility.HtmlEncode(codigoPublico)}</b>.</p>"
            + $"<p>Recógelo en <b>{WebUtility.HtmlEncode(organismo)}</b>.</p>"
            + "<p style=\"color:#555\">Guarda este código: con él puedes consultar el estado de tu trámite.</p></div>";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo enviar el correo de cierre ({Tipo}); se reintentará al consultar el cierre")]
    private static partial void Fallo(ILogger logger, string tipo);
}
