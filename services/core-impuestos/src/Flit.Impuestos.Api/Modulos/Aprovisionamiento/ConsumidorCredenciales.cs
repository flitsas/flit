using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Trabajos internos de credenciales de core-impuestos (M07b, plan §5.8; cola <see cref="Cola"/>, exchange <c>flit.impuestos</c>,
/// solo del propio servicio). El SDK abre la transacción y anota la bandeja, así que una re-entrega no repite el efecto; si el
/// manejo lanza, el SDK reintenta con su espera y al final lo deja en la DLQ.
/// <list type="bullet">
///   <item><see cref="TipoPropagar"/> (D-ALTA-IDEM): el alta rotó la llave; se lee la vigente (cifrada) y se manda a Diagnóstico
///   con <c>ActualizarCredencialImpuestoDiagnostico</c> (M04b). Sin llave de Diagnóstico termina sin efecto. Identidad caída
///   ⇒ reintento; un rechazo definitivo del rpc (argumento o permiso) ⇒ DLQ sin reintentos.</item>
///   <item><see cref="TipoRefrescarSecreto"/> (punto 7): se encendió <c>modo_respaldo_firma</c>; un alta trae el secreto vigente.
///   Si cambió, se guarda (el anterior queda 24 h) y se apaga el modo respaldo; si es el mismo, el modo sigue. Solo en
///   <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c>; el alta que no responde se reintenta.</item>
/// </list>
/// Nunca registra la llave ni el secreto.
/// </summary>
/// <example>
/// <code>
/// guardado.Encolar(db, ConsumidorCredenciales.TipoPropagar, empresa); // y SaveChanges en la misma transacción
/// </code>
/// </example>
public sealed partial class ConsumidorCredenciales(
    EmpresaDelTrabajo empresaDelTrabajo,
    ImpuestosDb db,
    AlmacenCredenciales almacen,
    IClienteIdentidadAlta identidad,
    AltaEmpresaApi alta,
    GuardadoCredencialAlta guardado,
    TimeProvider reloj,
    ILogger<ConsumidorCredenciales> log) : IEventConsumer<JsonElement>
{
    public const string Cola = "impuestos.credenciales";
    public const string TipoPropagar = "impuestos.credencial.propagar";
    public const string TipoRefrescarSecreto = "impuestos.secreto.refrescar";

    /// <summary>Único productor aceptado: el propio servicio.</summary>
    public const string Productor = "impuestos";

    public async Task HandleAsync(EventEnvelope envelope, JsonElement data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Type is not (TipoPropagar or TipoRefrescarSecreto))
        {
            Ignorado(log, envelope.EventId, envelope.Type);
            return;
        }

        var trabajo = ConsumidorCambioProducto.Leer<TrabajoCredencial>(envelope, data);
        if (trabajo.TenantId != envelope.TenantId)
            throw new SinReintentoAutomaticoException("La empresa de los datos no es la del sobre.");
        empresaDelTrabajo.Fijar(envelope.TenantId);
        if (envelope.Type == TipoPropagar)
            await PropagarAsync(envelope.TenantId, ct).ConfigureAwait(false);
        else
            await RefrescarAsync(envelope.TenantId, ct).ConfigureAwait(false);
    }

    private async Task PropagarAsync(Guid empresa, CancellationToken ct)
    {
        var credencial = await almacen.LeerAsync(db, empresa, ct).ConfigureAwait(false);
        if (credencial is null)
        {
            SinEfecto(log, empresa, "sin credencial");
            return;
        }

        bool actualizada;
        try
        {
            actualizada = await identidad.ActualizarLlaveDiagnosticoAsync(empresa, credencial.ApiKey, ct).ConfigureAwait(false);
        }
        catch (IdentidadNoDisponibleException ex) when (ex.InnerException is global::Grpc.Core.RpcException
        {
            StatusCode: global::Grpc.Core.StatusCode.InvalidArgument or global::Grpc.Core.StatusCode.PermissionDenied,
        })
        {
            throw new SinReintentoAutomaticoException($"Identidad rechazó la propagación: {ex.Message}");
        }

        Propagada(log, empresa, actualizada);
    }

    private async Task RefrescarAsync(Guid empresa, CancellationToken ct)
    {
        var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is null || !TransicionesAprovisionamiento.Liquida(fila.Estado))
        {
            SinEfecto(log, empresa, fila is null ? "sin aprovisionamiento" : $"estado {fila.Estado}");
            return;
        }

        var resultado = await alta.EjecutarAsync(empresa, ct).ConfigureAwait(false);
        if (resultado.Credenciales is null)
        {
            if (resultado.Evento == EventoAprovisionamiento.NoSalio)
                throw new AltaNoDisponibleException();
            AltaSinCredenciales(log, empresa, resultado.Evento);
            return;
        }

        if (resultado.Motivo == MotivosAprovisionamiento.EmpresaInactivaEnProveedor)
            Inactiva(log, empresa);
        var (credencial, secretoNuevo) = await guardado.EscribirAsync(db, empresa, resultado, ct).ConfigureAwait(false);
        if (secretoNuevo)
        {
            fila.ModoRespaldoFirma = false;
            fila.FirmasInvalidasConsecutivas = 0;
            fila.ActualizadoEn = reloj.GetUtcNow();
        }

        guardado.TrasEscribir(db, empresa, resultado, fila.Estado, credencial.ActualizadoEn);
        Refrescado(log, empresa, secretoNuevo, resultado.ClaveRotada);
    }

    /// <summary>El alta del refresco no salió o no respondió: el SDK reintenta el trabajo.</summary>
    internal sealed class AltaNoDisponibleException : Exception
    {
        public AltaNoDisponibleException()
            : base("El alta del proveedor no respondió; se reintenta el refresco del secreto.")
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Llave rotada de la empresa {Empresa} propagada a Diagnóstico (había llave de Diagnóstico: {Actualizada}).")]
    private static partial void Propagada(ILogger logger, Guid empresa, bool actualizada);

    [LoggerMessage(Level = LogLevel.Information, Message = "Secreto del webhook de la empresa {Empresa} refrescado con el alta (cambió: {Cambio}; llave rotada: {Rotada}).")]
    private static partial void Refrescado(ILogger logger, Guid empresa, bool cambio, bool rotada);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta de refresco de la empresa {Empresa} la trae desactivada en el proveedor; las credenciales se guardan.")]
    private static partial void Inactiva(ILogger logger, Guid empresa);

    [LoggerMessage(Level = LogLevel.Warning, Message = "El alta de refresco de la empresa {Empresa} no trajo credenciales ({Evento}); el secreto no cambia.")]
    private static partial void AltaSinCredenciales(ILogger logger, Guid empresa, EventoAprovisionamiento evento);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trabajo de credenciales de la empresa {Empresa} sin efecto: {Motivo}.")]
    private static partial void SinEfecto(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trabajo {EventId} ({Tipo}) sin efecto para credenciales.")]
    private static partial void Ignorado(ILogger logger, Guid eventId, string tipo);
}
