using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Identidad;

/// <summary>
/// Desenlace de una operación de identidad. Éxito o un error con estado HTTP y código estable para el front (nunca un dato
/// personal). <see cref="Aceptada"/> = 202: FLIT 2 no respondió y el servicio reintentará solo (<c>pendiente_envio</c>).
/// </summary>
public sealed record ResultadoIdentidad(
    bool Exito,
    int Http,
    string Codigo,
    string Mensaje,
    string? Parte = null,
    short Ordinal = 1,
    string? Estado = null,
    string? Campo = null,
    int? EsperaSegundos = null,
    bool Aceptada = false)
{
    public static ResultadoIdentidad Ok(string parte, short ordinal, string estado, bool aceptada = false) =>
        new(true, aceptada ? StatusCodes.Status202Accepted : StatusCodes.Status200OK, "ok", string.Empty, parte, ordinal, estado, Aceptada: aceptada);

    public static ResultadoIdentidad Error(int http, string codigo, string mensaje, string? estado = null, string? campo = null, int? espera = null) =>
        new(false, http, codigo, mensaje, Estado: estado, Campo: campo, EsperaSegundos: espera);
}


public sealed class IdentidadOptions
{
    public const string SectionName = "Identidad";

    /// <summary>Reenvíos del enlace por parte y solicitud (HU #14045 AC2).</summary>
    public int MaxReenvios { get; set; } = 5;

    /// <summary>Espera mínima entre dos reenvíos de la misma parte.</summary>
    public int EsperaReenvioSegundos { get; set; } = 60;

    /// <summary>Con el bus apagado o un evento perdido, <c>GET /identidad</c> y la compuerta preguntan a FLIT 2 por lo pendiente.</summary>
    public bool ReconciliarAlConsultar { get; set; } = true;

    /// <summary>Cada cuánto el servicio reintenta los envíos que quedaron en <c>pendiente_envio</c>.</summary>
    public int ReintentoSegundos { get; set; } = 30;

    public int ArranqueSegundos { get; set; } = 30;

    public int LoteReintento { get; set; } = 50;
}

/// <summary>
/// Validación de identidad de las partes de una solicitud del portal (HU #14044, ADR-0075). El BFF no valida nada:
/// pide el enlace a FLIT 2 (<see cref="IFachadaIdentidad"/>), que es el dueño del proveedor y de la captura, y refleja el
/// estado. Decisión del PO (2026-10-27): cada solicitud pide una validación nueva a cada parte; el estado guardado vale solo
/// para su solicitud y no tiene vigencia en días. El contacto se lee únicamente por <see cref="PersonaService"/> y no se
/// loguea. Dos peticiones iguales no duplican el enlace: FLIT 2 es idempotente por (solicitud, parte, documento).
/// </summary>
public sealed partial class IdentidadService(
    PersonaNaturalDb db,
    PersonaService personas,
    IReglasDeIdentidad reglas,
    IFachadaIdentidad fachada,
    EspejoDeIdentidad espejo,
    Microsoft.Extensions.Options.IOptions<IdentidadOptions> opciones,
    TimeProvider reloj,
    ILogger<IdentidadService> logger)
{
    private const string ActorLectura = "identidad";
    private const string ServicioNoDisponible = FachadaIdentidadGrpc.ServicioNoDisponible;
    private static readonly string[] EstadosEditables = [EstadoSolicitud.Borrador, EstadoSolicitud.PendienteValidacion];

    private readonly IdentidadOptions _opciones = opciones.Value;

    /// <summary>
    /// <c>POST /solicitudes/{id}/identidad/{parte}</c>: pide la validación de esa parte. Con una en curso responde 409
    /// <c>validacion_en_curso</c> con el estado y no pide otra; con la aprobada de ESTA solicitud responde su estado; con una
    /// rechazada o vencida pide una nueva. Nunca mira lo que esa cédula validó en otro trámite.
    /// </summary>
    public async Task<ResultadoIdentidad> SolicitarAsync(Guid solicitudId, string parte, short ordinal, CancellationToken ct)
    {
        var preparado = await PrepararAsync(solicitudId, parte, ordinal, ct).ConfigureAwait(false);
        if (preparado.Error is not null)
            return preparado.Error;
        var (persona, rol) = (preparado.Persona!, preparado.Rol!);

        if (persona.ValidacionEstado == EstadosIdentidad.Aprobada)
            return ResultadoIdentidad.Ok(rol, ordinal, EstadosIdentidad.Aprobada);
        if (EstadosIdentidad.EnCurso(persona.ValidacionEstado))
            return ResultadoIdentidad.Error(StatusCodes.Status409Conflict, "validacion_en_curso", "Ya hay una validación en curso para esta persona.", persona.ValidacionEstado);

        return await EnviarAsync(persona, rol, preparado.Datos!, nuevaValidacion: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reintenta, para el job del servicio, los envíos que quedaron en <c>pendiente_envio</c> (FLIT 2 no respondió). Sin
    /// duplicar el enlace: FLIT 2 devuelve la misma validación si ya había una para esa solicitud, parte y documento.
    /// Devuelve cuántas quedaron enviadas.
    /// </summary>
    public async Task<int> ReintentarPendientesAsync(CancellationToken ct)
    {
        var limite = reloj.GetUtcNow().AddSeconds(-_opciones.ReintentoSegundos);
        var candidatas = await (from p in db.Set<PersonaSolicitante>().AsNoTracking()
                                join s in db.Set<SolicitudTramite>().AsNoTracking() on p.SolicitudId equals s.Id
                                where p.ValidacionEstado == EstadosIdentidad.PendienteEnvio && p.UpdatedAt <= limite && EstadosEditables.Contains(s.Estado)
                                orderby p.UpdatedAt
                                select new { p.SolicitudId, p.Parte, p.Ordinal }).Take(_opciones.LoteReintento).ToListAsync(ct).ConfigureAwait(false);

        var enviadas = 0;
        foreach (var c in candidatas)
        {
            db.ChangeTracker.Clear();
            var preparado = await PrepararAsync(c.SolicitudId, c.Parte, c.Ordinal, ct).ConfigureAwait(false);
            if (preparado.Error is not null || preparado.Persona!.ValidacionEstado != EstadosIdentidad.PendienteEnvio)
                continue;
            var resultado = await EnviarAsync(preparado.Persona, preparado.Rol!, preparado.Datos!, nuevaValidacion: false, ct, origen: "servicio").ConfigureAwait(false);
            if (resultado is { Exito: true, Aceptada: false })
                enviadas++;
        }

        return enviadas;
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Preparado(ResultadoIdentidad? Error, PersonaSolicitante? Persona, string? Rol, DatosPersona? Datos);

    /// <summary>Lo común de pedir y reenviar: la solicitud editable, el rol que exige identidad, la persona y su contacto válido.</summary>
    private async Task<Preparado> PrepararAsync(Guid solicitudId, string parte, short ordinal, CancellationToken ct)
    {
        var estadoSolicitud = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == solicitudId).Select(s => s.Estado).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (estadoSolicitud is null)
            return Falla(StatusCodes.Status404NotFound, "solicitud_no_encontrada", "No encontramos ese trámite.");
        if (!EstadosEditables.Contains(estadoSolicitud))
            return Falla(StatusCodes.Status409Conflict, "solicitud_no_editable", "Este trámite ya no admite cambios.");

        var definidas = await reglas.LeerAsync(solicitudId, ct).ConfigureAwait(false);
        var rolPedido = parte?.Trim() ?? string.Empty;
        if (definidas is null || !definidas.Existe(rolPedido))
            return Falla(StatusCodes.Status404NotFound, "rol_desconocido", "Ese rol no existe en este trámite.");
        // AC5: un rol que la definición no marca con validación no recibe enlace ni cuenta como pendiente.
        if (definidas.Exige(rolPedido) is not { } regla)
            return Falla(StatusCodes.Status422UnprocessableEntity, "rol_sin_validacion", "Este rol no necesita validar su identidad.");

        var persona = await db.Set<PersonaSolicitante>().FirstOrDefaultAsync(
            p => p.SolicitudId == solicitudId && p.Parte == regla.Rol && p.Ordinal == ordinal, ct).ConfigureAwait(false);
        if (persona is null)
            return Falla(StatusCodes.Status409Conflict, "actor_no_registrado", "Registre primero a esta persona en el trámite.");

        // AC6: un NIT es persona jurídica, fuera del alcance del portal. Se rechaza sin llamar a FLIT 2.
        if (string.Equals(persona.TipoDocumento.Trim(), "NIT", StringComparison.OrdinalIgnoreCase))
            return Falla(StatusCodes.Status422UnprocessableEntity, "persona_juridica_no_soportada", "El portal solo atiende personas naturales.");

        var datos = (await personas.LeerAsync(solicitudId, ActorLectura, ct).ConfigureAwait(false))
            .FirstOrDefault(d => string.Equals(d.Parte, regla.Rol, StringComparison.OrdinalIgnoreCase) && d.Ordinal == ordinal);
        if (datos is null)
            return Falla(StatusCodes.Status409Conflict, "actor_no_registrado", "Registre primero a esta persona en el trámite.");

        // AC4: contacto inválido, con el campo que falla y sin llamar a FLIT 2.
        if (!CorreoValido(datos.Correo))
            return Falla(StatusCodes.Status422UnprocessableEntity, "contacto_invalido", "Escriba un correo válido para esta persona.", campo: "correo");
        if (!string.IsNullOrWhiteSpace(datos.Celular) && !CelularValido(datos.Celular))
            return Falla(StatusCodes.Status422UnprocessableEntity, "contacto_invalido", "Escriba un celular válido para esta persona.", campo: "celular");

        return new Preparado(null, persona, regla.Rol, datos);
    }

    /// <summary>Pide el envío a FLIT 2 y refleja el resultado; sin respuesta de FLIT 2 deja <c>pendiente_envio</c> (202).</summary>
    private async Task<ResultadoIdentidad> EnviarAsync(
        PersonaSolicitante persona, string rol, DatosPersona datos, bool nuevaValidacion, CancellationToken ct, string origen = "ciudadano")
    {
        try
        {
            var respuesta = await fachada.SolicitarAsync(
                persona.SolicitudId, rol, new PersonaParaValidar(datos.TipoDocumento, datos.Documento, datos.Nombre ?? string.Empty, datos.Correo!, datos.Celular), ct).ConfigureAwait(false);
            if (nuevaValidacion)
            {
                persona.Reenvios = 0;
                persona.UltimoReenvioEn = null;
            }

            persona.ValidacionId = respuesta.ValidacionId;
            await espejo.CambiarAsync(persona, respuesta.Estado, origen, null, ct).ConfigureAwait(false);
            return ResultadoIdentidad.Ok(rol, persona.Ordinal, persona.ValidacionEstado);
        }
        catch (FachadaException ex) when (ex.Codigo == ServicioNoDisponible)
        {
            // AC7: FLIT 2 sin respuesta. El actor queda pendiente_envio y el job reintenta; sin duplicar el enlace.
            await espejo.CambiarAsync(persona, EstadosIdentidad.PendienteEnvio, origen, null, ct).ConfigureAwait(false);
            EnvioPendiente(logger, persona.SolicitudId);
            return ResultadoIdentidad.Ok(rol, persona.Ordinal, EstadosIdentidad.PendienteEnvio, aceptada: true);
        }
        catch (FachadaException ex)
        {
            return Traducir(ex, persona.ValidacionEstado);
        }
    }

    private static ResultadoIdentidad Traducir(FachadaException ex, string estadoActual) => ex.Codigo switch
    {
        "persona_juridica_no_soportada" => ResultadoIdentidad.Error(ex.EstadoHttp, ex.Codigo, ex.Message),
        "contacto_invalido_correo" => ResultadoIdentidad.Error(ex.EstadoHttp, "contacto_invalido", ex.Message, campo: "correo"),
        "validacion_en_curso" or "identidad_aprobada" => ResultadoIdentidad.Error(ex.EstadoHttp, ex.Codigo, ex.Message, estadoActual),
        ServicioNoDisponible => ResultadoIdentidad.Error(StatusCodes.Status503ServiceUnavailable, ServicioNoDisponible, ex.Message),
        _ => ResultadoIdentidad.Error(ex.EstadoHttp, ex.Codigo, ex.Message),
    };

    private static Preparado Falla(int http, string codigo, string mensaje, string? campo = null) =>
        new(ResultadoIdentidad.Error(http, codigo, mensaje, campo: campo), null, null, null);

    internal static bool CorreoValido(string? correo) =>
        !string.IsNullOrWhiteSpace(correo) && correo.Length <= 320
        && MailAddress.TryCreate(correo.Trim(), out var mail) && string.Equals(mail.Address, correo.Trim(), StringComparison.Ordinal);

    /// <summary>Celular colombiano: 10 dígitos que empiezan por 3, con o sin el 57 de país.</summary>
    internal static bool CelularValido(string? celular)
    {
        var digitos = Regex.Replace(celular ?? string.Empty, @"[\s\-+()]", string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (digitos.StartsWith("57", StringComparison.Ordinal) && digitos.Length == 12)
            digitos = digitos[2..];
        return digitos.Length == 10 && digitos[0] == '3' && digitos.All(char.IsAsciiDigit);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FLIT 2 no respondió al pedir la validación de una parte de la solicitud {SolicitudId}; queda pendiente_envio")]
    private static partial void EnvioPendiente(ILogger logger, Guid solicitudId);
}

/// <summary>
/// Refleja el estado de la validación de una parte (<c>persona_solicitante.validacion_estado</c>) y deja la auditoría. Lo usan
/// las solicitudes del ciudadano, el sondeo y el consumidor del evento de FLIT 2; todos pasan por aquí para que la regla de
/// transición sea una sola: <c>aprobada</c> es definitiva para esa validación (un aviso tardío no la deshace).
/// </summary>
public sealed class EspejoDeIdentidad(PersonaNaturalDb db, TimeProvider reloj)
{
    /// <summary>Estados que FLIT 2 puede reportar y el portal acepta; cualquier otro se ignora.</summary>
    private static readonly HashSet<string> Aceptados =
        [EstadosIdentidad.Pendiente, EstadosIdentidad.Aprobada, EstadosIdentidad.Rechazada, EstadosIdentidad.Vencida, EstadosIdentidad.PendienteEnvio];

    /// <summary>
    /// Aplica <paramref name="nuevoEstado"/> a la persona (ya cargada y rastreada) y guarda, junto con lo que el llamante
    /// haya cambiado en ella. Devuelve si el estado cambió. Idempotente: el mismo estado dos veces no repite la auditoría.
    /// </summary>
    public async Task<bool> CambiarAsync(PersonaSolicitante persona, string nuevoEstado, string origen, Guid? eventId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(persona);
        var ahora = reloj.GetUtcNow();
        var cambia = Aceptados.Contains(nuevoEstado) && persona.ValidacionEstado != nuevoEstado && persona.ValidacionEstado != EstadosIdentidad.Aprobada;
        if (cambia)
        {
            db.Add(new AuditoriaIdentidad
            {
                SolicitudId = persona.SolicitudId,
                Parte = persona.Parte,
                Ordinal = persona.Ordinal,
                Desde = persona.ValidacionEstado,
                Hasta = nuevoEstado,
                Origen = origen,
                EventId = eventId,
                OcurrioEn = ahora,
            });
            persona.ValidacionEstado = nuevoEstado;
        }

        persona.UpdatedAt = ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return cambia;
    }

    /// <summary>Refleja lo que FLIT 2 informó de una validación (evento o sondeo). Sin persona con ese id no hace nada.</summary>
    public async Task<bool> AplicarAsync(Guid validacionId, string nuevoEstado, string origen, Guid? eventId, CancellationToken ct)
    {
        var persona = await db.Set<PersonaSolicitante>().FirstOrDefaultAsync(p => p.ValidacionId == validacionId, ct).ConfigureAwait(false);
        return persona is not null && await CambiarAsync(persona, nuevoEstado, origen, eventId, ct).ConfigureAwait(false);
    }
}
