using Flit.PersonaNatural.Api.Abuso;
using Flit.PersonaNatural.Api.Consentimientos;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Consultas;

public enum EstadoPersonaDetalle
{
    Ok,
    SolicitudNoEncontrada,

    /// <summary>El rol no existe (o no admite persona natural) en la definición congelada de ESA solicitud. NO se llamó a core-consultas.</summary>
    RolInvalido,

    /// <summary>La solicitud ya gastó sus consultas de detalle.</summary>
    DetalleAgotado,

    DocumentoExcedido,
    TopeGlobal,
}

public sealed record ResultadoPersonaDetalle(EstadoPersonaDetalle Estado, PersonaDetalle? Persona = null);

/// <summary>
/// Consulta de persona CON nombres para rellenar el actor del trámite (HU #14060, el prellenado por documento de FLIT 2).
/// Revela datos de un tercero en un portal público, así que cada paso tiene su freno y el ORDEN importa: primero lo barato
/// y local (rol del snapshot, tope por solicitud), luego los topes por documento y globales, y solo entonces core-consultas.
/// La sesión de la solicitud y su estado editable los verifica el filtro del endpoint antes de llegar aquí.
/// <list type="bullet">
/// <item>Tope por solicitud (<c>Consultas:MaxDetallePorSolicitud</c>, 6 por defecto): se reserva insertando la fila de
/// auditoría y contando; dos peticiones simultáneas nunca superan el tope (a lo sumo ambas se rechazan).</item>
/// <item>Cada llamada queda en <c>acceso_pii_log</c> (actor <c>sesion</c>, acción <see cref="Accion"/>, solicitud y huella
/// de IP): jamás el documento ni los nombres. Una llamada rechazada por el tope por documento o global no gasta cupo.</item>
/// <item>Sin logs: este servicio no escribe ninguno y los mensajes de excepción nunca llevan datos de la consulta.</item>
/// </list>
/// </summary>
public sealed class ConsultaPersonaDetalleService(
    PersonaNaturalDb db, CuotaService cuotas, ConsentimientoService consentimientos, PiiHasher hasher, IConsultasRunt runt,
    MedicionConsultasService medicion, TimeProvider reloj, IConfiguration configuration)
{
    public const string Accion = "consulta_persona_detalle";
    public const string AccionAgotada = "consulta_persona_detalle_agotada";
    public const string AccionRechazada = "consulta_persona_detalle_rechazada";
    public const string ActorAuditoria = "sesion";
    public const string ClaveMaxDetalle = "Consultas:MaxDetallePorSolicitud";
    public const int MaxDetallePorDefecto = 6;

    /// <summary>Tipos que el prellenado de FLIT 2 acepta para una persona natural (sin NIT).</summary>
    public static readonly IReadOnlySet<string> TiposDocumento = new HashSet<string>(StringComparer.Ordinal) { "CC", "CE", "PAS", "TI" };

    /// <summary>
    /// <paramref name="documento"/> ya validado con <see cref="EntradaConsulta.Documento"/>; <paramref name="rol"/> en minúsculas.
    /// </summary>
    /// <exception cref="ProveedorNoDisponibleException">core-consultas no respondió o el proveedor del RUNT estaba caído.</exception>
    public async Task<ResultadoPersonaDetalle> ConsultarAsync(
        Guid solicitudId, DocumentoConsulta documento, string rol, string version, byte[]? ipHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var snapshot = await db.Set<SolicitudTramite>().AsNoTracking().Where(s => s.Id == solicitudId)
            .Select(s => s.DefinicionSnapshot).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (snapshot is null)
            return new ResultadoPersonaDetalle(EstadoPersonaDetalle.SolicitudNoEncontrada);

        var definicion = DefinicionesService.Leer(snapshot);
        if (definicion is null || !definicion.Actores.Any(a => a.PersonaNaturalPermitida && string.Equals(a.Rol, rol, StringComparison.Ordinal)))
            return new ResultadoPersonaDetalle(EstadoPersonaDetalle.RolInvalido);

        var fila = new AccesoPiiLog { Actor = ActorAuditoria, Accion = Accion, SolicitudId = solicitudId, IpHash = ipHash, OcurrioEn = reloj.GetUtcNow() };
        db.Add(fila);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var maximo = Math.Max(1, configuration.GetValue<int?>(ClaveMaxDetalle) ?? MaxDetallePorDefecto);
        var usadas = await db.Set<AccesoPiiLog>().CountAsync(a => a.SolicitudId == solicitudId && a.Accion == Accion, ct).ConfigureAwait(false);
        if (usadas > maximo)
        {
            await CambiarAccionAsync(fila, AccionAgotada, ct).ConfigureAwait(false);
            return new ResultadoPersonaDetalle(EstadoPersonaDetalle.DetalleAgotado);
        }

        // El mismo tope por documento y global que las consultas públicas: la cédula consultada se cuenta una sola vez, venga por donde venga.
        var cuota = await cuotas.ReservarConsultaAsync(documento.Numero, ipHash, ct).ConfigureAwait(false);
        if (cuota != ResultadoCuota.Permitida)
        {
            await CambiarAccionAsync(fila, AccionRechazada, ct).ConfigureAwait(false);
            return new ResultadoPersonaDetalle(cuota == ResultadoCuota.CedulaExcedida ? EstadoPersonaDetalle.DocumentoExcedido : EstadoPersonaDetalle.TopeGlobal);
        }

        await consentimientos.RegistrarConsultaAsync(hasher.HashCedula(documento.Numero), version, ipHash, ct).ConfigureAwait(false);

        // La medición se cuenta en cuanto se llama a core-consultas, salga bien o mal.
        try
        {
            var resultado = await runt.ConsultarConductorAsync(Proveedor(documento), solicitudId.ToString(), ct).ConfigureAwait(false);
            if (ProyeccionMinima.ProveedorCaido(resultado))
                throw new ProveedorNoDisponibleException("El proveedor del RUNT no respondió.");

            return new ResultadoPersonaDetalle(EstadoPersonaDetalle.Ok, ProyeccionMinima.PersonaConNombres(resultado));
        }
        finally
        {
            await medicion.RegistrarAsync(MedicionConsultasService.TipoPersonaDetalle, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>El mismo mapeo de tipo que el prellenado de FLIT 2 hacia el proveedor (PAS→PA, TI→PPT).</summary>
    private static DocumentoConsulta Proveedor(DocumentoConsulta documento) => documento.Tipo switch
    {
        "PAS" => documento with { Tipo = "PA" },
        "TI" => documento with { Tipo = "PPT" },
        _ => documento,
    };

    private async Task CambiarAccionAsync(AccesoPiiLog fila, string accion, CancellationToken ct)
    {
        fila.Accion = accion;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
