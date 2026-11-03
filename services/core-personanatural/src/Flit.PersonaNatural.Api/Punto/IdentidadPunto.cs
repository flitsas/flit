using System.Text.Json;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Identidad e invitaciones del canal del punto (HU #14237). Requiere cookie del punto y sesión del borrador
/// (<c>Authorization: Bearer</c> / cookie <c>pn_sesion</c> vía proxy). El radicante valida in-situ; otras partes
/// reciben correo + URL copiable.
/// </summary>
public sealed class IdentidadPuntoService(
    PersonaNaturalDb db,
    IdentidadService identidad,
    SolicitudesService solicitudes,
    SesionService sesiones)
{
    public const string CodigoNoDueño = "no_es_propietario";
    public const string CodigoInvitacionesBloqueadas = "invitaciones_bloqueadas";
    public const string MensajeNoDueño = ActoresPuntoService.MensajeNoDueño;

    public async Task<ResultadoIdentidad> RadicanteInSituAsync(
        Guid solicitudId,
        string? parte,
        short ordinal,
        string? tipoDocumento,
        string? documento,
        string? nombre,
        string? correoCiudadano,
        string? celular,
        string? direccion,
        string? ciudad,
        CancellationToken ct)
    {
        var gate = await CompuertaRadicanteAsync(solicitudId, ct).ConfigureAwait(false);
        if (gate is not null)
            return gate;

        var rol = string.IsNullOrWhiteSpace(parte) ? await RolRadicanteAsync(solicitudId, ct).ConfigureAwait(false) : parte.Trim();
        if (rol is null)
            return ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, "rol_desconocido", "No hay un rol de radicante en este trámite.");

        if (string.IsNullOrWhiteSpace(documento))
            return ResultadoIdentidad.Error(StatusCodes.Status400BadRequest, "datos_invalidos", "Indique el documento de quien radica.");

        // Correo: si el ciudadano ya lo digitó en la ficha se guarda; si no, técnico (in-situ no notifica).
        var correo = string.IsNullOrWhiteSpace(correoCiudadano)
            ? $"punto-{solicitudId:N}@noreply.flit.local"
            : correoCiudadano.Trim();
        try
        {
            await solicitudes.GuardarActoresAsync(solicitudId, new ActoresRequest(null, [
                new ActorRequest(
                    rol,
                    ordinal,
                    string.IsNullOrWhiteSpace(tipoDocumento) ? "CC" : tipoDocumento.Trim(),
                    documento.Trim(),
                    string.IsNullOrWhiteSpace(nombre) ? "Radicante en el punto" : nombre.Trim(),
                    correo,
                    string.IsNullOrWhiteSpace(celular) ? null : celular.Trim(),
                    string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim(),
                    string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim()),
            ]), ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex)
        {
            return ResultadoIdentidad.Error(ex.Estado, ex.Codigo, ex.Message);
        }

        return await identidad.SolicitarInSituAsync(solicitudId, rol, ordinal, ct).ConfigureAwait(false);
    }

    public Task<EstadoIdentidad?> EstadoAsync(Guid solicitudId, CancellationToken ct) =>
        identidad.EstadoAsync(solicitudId, ct);

    /// <summary>
    /// Guarda otra parte (documento + correo), solicita validación con enlace y expone <c>capturaUrl</c> copiable.
    /// Bloqueado si el radicante no está aprobado.
    /// </summary>
    public async Task<ResultadoIdentidad> InvitarAsync(Guid solicitudId, InvitarPartePuntoRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var gate = await CompuertaRadicanteAsync(solicitudId, ct).ConfigureAwait(false);
        if (gate is not null)
            return gate;

        var estado = await identidad.EstadoAsync(solicitudId, ct).ConfigureAwait(false);
        var rolRadicante = await RolRadicanteAsync(solicitudId, ct).ConfigureAwait(false);
        var radicanteOk = estado?.Partes.Any(p =>
            rolRadicante is not null
            && string.Equals(p.Parte, rolRadicante, StringComparison.OrdinalIgnoreCase)
            && p.Estado == EstadosIdentidad.Aprobada) == true;
        if (!radicanteOk)
            return ResultadoIdentidad.Error(
                StatusCodes.Status409Conflict,
                CodigoInvitacionesBloqueadas,
                "Primero debe quedar identificada la persona que radica.",
                estado: estado?.Partes.FirstOrDefault(p => rolRadicante is not null && string.Equals(p.Parte, rolRadicante, StringComparison.OrdinalIgnoreCase))?.Estado);

        var parte = (body.Parte ?? string.Empty).Trim();
        if (parte.Length == 0)
            return ResultadoIdentidad.Error(StatusCodes.Status400BadRequest, "datos_invalidos", "Indique el rol de la parte a invitar.");

        var actores = new ActoresRequest(null, [
            new ActorRequest(
                parte,
                body.Ordinal ?? 1,
                body.TipoDocumento,
                body.Documento,
                body.Nombre,
                body.Correo,
                body.Celular,
                body.Direccion,
                body.Ciudad),
        ]);

        try
        {
            await solicitudes.GuardarActoresAsync(solicitudId, actores, ct).ConfigureAwait(false);
        }
        catch (ReglaDeNegocioException ex)
        {
            return ResultadoIdentidad.Error(ex.Estado, ex.Codigo, ex.Message);
        }

        return await identidad.SolicitarAsync(solicitudId, parte, body.Ordinal ?? 1, ct, exponerCaptura: true).ConfigureAwait(false);
    }

    /// <summary>Valida token de sesión de solicitud y que el borrador sea del canal punto.</summary>
    public async Task<(Guid SolicitudId, ResultadoIdentidad? Error)> ResolverSolicitudAsync(string? token, Guid? solicitudIdCuerpo, CancellationToken ct)
    {
        var validada = await sesiones.ValidarAsync(token, ct).ConfigureAwait(false);
        if (validada.Estado != EstadoSesion.Valida)
            return (default, ResultadoIdentidad.Error(StatusCodes.Status401Unauthorized, "session_invalid", "La sesión del trámite no es válida."));

        var id = validada.SolicitudId;
        if (solicitudIdCuerpo is { } pedida && pedida != Guid.Empty && pedida != id)
            return (default, ResultadoIdentidad.Error(StatusCodes.Status403Forbidden, "solicitud_ajena", "La sesión no corresponde a esa solicitud."));

        var canal = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => s.Datos)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (canal is null)
            return (default, ResultadoIdentidad.Error(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontramos ese trámite."));
        if (!EsCanalPunto(canal))
            return (default, ResultadoIdentidad.Error(StatusCodes.Status409Conflict, "no_es_canal_punto", "Esta solicitud no se abrió en el punto de atención."));

        return (id, null);
    }

    private async Task<ResultadoIdentidad?> CompuertaRadicanteAsync(Guid solicitudId, CancellationToken ct)
    {
        var datos = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Id == solicitudId)
            .Select(s => s.Datos)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (datos is null)
            return ResultadoIdentidad.Error(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontramos ese trámite.");
        if (!EsCanalPunto(datos))
            return ResultadoIdentidad.Error(StatusCodes.Status409Conflict, "no_es_canal_punto", "Esta solicitud no se abrió en el punto de atención.");
        return null;
    }

    private async Task<string?> RolRadicanteAsync(Guid solicitudId, CancellationToken ct)
    {
        var definidas = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Id == solicitudId)
            .Select(s => s.DefinicionSnapshot)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(definidas))
            return null;
        // Preferencia alineada al portal: vendedor → comprador → locatario → primer rol con identidad.
        foreach (var candidato in new[] { "vendedor", "comprador", "locatario" })
        {
            if (definidas.Contains($"\"{candidato}\"", StringComparison.OrdinalIgnoreCase)
                || definidas.Contains($"\"role\":\"{candidato}\"", StringComparison.OrdinalIgnoreCase)
                || definidas.Contains($"\"rol\":\"{candidato}\"", StringComparison.OrdinalIgnoreCase))
                return candidato;
        }

        var persona = await db.Set<PersonaSolicitante>().AsNoTracking()
            .Where(p => p.SolicitudId == solicitudId)
            .OrderBy(p => p.Ordinal)
            .Select(p => p.Parte)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(persona) ? null : persona;
    }

    private static bool EsCanalPunto(string datosJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(datosJson) ? "{}" : datosJson);
            if (!doc.RootElement.TryGetProperty("canal", out var canal))
                return false;
            return string.Equals(canal.GetString(), ActoresPuntoService.CanalOrigen, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record InvitarPartePuntoRequest(
    string? Parte,
    short? Ordinal,
    string? TipoDocumento,
    string? Documento,
    string? Nombre,
    string? Correo,
    string? Celular,
    string? Direccion = null,
    string? Ciudad = null);
