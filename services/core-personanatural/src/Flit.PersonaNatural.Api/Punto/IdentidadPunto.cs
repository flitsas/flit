using System.Text.Json;
using Flit.PersonaNatural.Api.Consultas;
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
    SesionService sesiones,
    IConsultasRunt runt,
    Microsoft.Extensions.Configuration.IConfiguration? configuracion = null,
    Flit.PersonaNatural.Api.Pruebas.IPruebasPortal? pruebas = null)
{
    public const string CodigoRolDesconocido = "rol_desconocido";
    public const string CodigoRolRadicante = "rol_radicante";
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

        var rol = await RolRadicanteAsync(solicitudId, ct).ConfigureAwait(false);
        if (rol is null)
            return ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoRolDesconocido, "No hay un rol de radicante en este trámite.");
        if (!string.IsNullOrWhiteSpace(parte) && !string.Equals(parte.Trim(), rol, StringComparison.OrdinalIgnoreCase))
        {
            var existe = (await RolesAsync(solicitudId, ct).ConfigureAwait(false)).Any(r => string.Equals(r.Rol, parte.Trim(), StringComparison.OrdinalIgnoreCase));
            return existe
                ? ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoRolRadicante, "Ese rol no es el de quien radica en el punto.")
                : ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoRolDesconocido, "Este tipo de trámite no tiene ese rol.");
        }

        if (string.IsNullOrWhiteSpace(documento))
            return ResultadoIdentidad.Error(StatusCodes.Status400BadRequest, "datos_invalidos", "Indique el documento de quien radica.");

        // Compuerta de dueño (#14236): el documento de quien se identifica debe ser UN propietario del RUNT (copropiedad: cualquiera).
        var placa = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Id == solicitudId).Select(s => s.Placa).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var propietario = EntradaConsulta.DocumentoPropietario(string.IsNullOrWhiteSpace(tipoDocumento) ? "CC" : tipoDocumento, documento);
        if (propietario is null || string.IsNullOrWhiteSpace(placa))
            return ResultadoIdentidad.Error(StatusCodes.Status400BadRequest, "datos_invalidos", "Revisa el documento de quien radica.", campo: "documento");
        try
        {
            var consulta = await runt.ConsultarVehiculoAsync(placa, null, propietario, ActoresPuntoService.CanalOrigen, ct).ConfigureAwait(false);
            if (ProyeccionMinima.ProveedorCaido(consulta))
                throw new ProveedorNoDisponibleException(TiposDelPunto.RuntCaido);
            if (!ActoresPuntoService.EsPropietarioRunt(consulta, propietario))
                return ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoNoDueño, MensajeNoDueño, campo: "documento");
        }
        catch (ProveedorNoDisponibleException)
        {
            return ResultadoIdentidad.Error(StatusCodes.Status503ServiceUnavailable, "proveedor_no_disponible", TiposDelPunto.RuntCaido);
        }

        // Correo: si el ciudadano ya lo digitó en la ficha se guarda; si no, técnico (in-situ no notifica).
        var correo = !IdentidadService.CorreoUtilizableInSitu(correoCiudadano)
            ? CorreoTecnicoPunto.Para(configuracion, solicitudId)
            : correoCiudadano!.Trim();
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
        if (!radicanteOk && pruebas?.OmitirIdentidad != true)
            return ResultadoIdentidad.Error(
                StatusCodes.Status409Conflict,
                CodigoInvitacionesBloqueadas,
                "Primero debe quedar identificada la persona que radica.",
                estado: estado?.Partes.FirstOrDefault(p => rolRadicante is not null && string.Equals(p.Parte, rolRadicante, StringComparison.OrdinalIgnoreCase))?.Estado);

        var parte = (body.Parte ?? string.Empty).Trim();
        if (parte.Length == 0)
            return ResultadoIdentidad.Error(StatusCodes.Status400BadRequest, "datos_invalidos", "Indique el rol de la parte a invitar.");
        var definido = (await RolesAsync(solicitudId, ct).ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Rol, parte, StringComparison.OrdinalIgnoreCase));
        if (definido is null)
            return ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoRolDesconocido, "Este tipo de trámite no tiene ese rol.");
        parte = definido.Rol;
        if (definido.EsRadicante && (body.Ordinal ?? 1) == 1)
            return ResultadoIdentidad.Error(StatusCodes.Status422UnprocessableEntity, CodigoRolRadicante, "Esa posición es la de quien radica; solo se pueden invitar las demás partes.");

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

    /// <summary>El radicante es el primer rol de la definición del tipo (ordinal 1); ningún rol se nombra en el BFF.</summary>
    private async Task<string?> RolRadicanteAsync(Guid solicitudId, CancellationToken ct) =>
        (await RolesAsync(solicitudId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.EsRadicante)?.Rol;

    private async Task<IReadOnlyList<RolPuntoDto>> RolesAsync(Guid solicitudId, CancellationToken ct)
    {
        var snapshot = await db.Set<SolicitudTramite>().AsNoTracking()
            .Where(s => s.Id == solicitudId)
            .Select(s => s.DefinicionSnapshot)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return RolesPunto.Desde(snapshot);
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
