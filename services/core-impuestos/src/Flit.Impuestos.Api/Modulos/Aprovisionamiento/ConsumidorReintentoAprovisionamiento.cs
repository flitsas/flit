using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>Datos de <c>impuestos.aprovisionamiento.reintentar</c> v1 (<c>impuestos-events.v1.yaml</c>, M03).</summary>
public sealed record AprovisionamientoReintentar(Guid TenantId, Guid SolicitadoPor);

/// <summary>
/// «Reintentar» del Super Admin (plan §3.1): desde <c>ERROR</c>, <c>INCIERTO</c> o <c>REQUIERE_REVISION_NIT</c> pasa a
/// <c>PENDIENTE</c> sin intentos ni espera (la ronda siguiente lo evalúa), sube la versión y deja historial con actor
/// <c>SUPER_ADMIN</c> y su id. Llega por dos caminos con la misma regla (<see cref="Reintentar"/>): el trabajo de la plataforma
/// (admin de compañías, en la transacción del consumidor de <see cref="ConsumidorCambioProducto.Cola"/>; en otro estado o sin fila
/// se confirma sin efecto) y la ruta del backoffice de Impuestos (409 <c>ESTADO_NO_REINTENTABLE</c> si no se puede).
/// </summary>
/// <example>
/// <code>
/// if (!ConsumidorReintentoAprovisionamiento.Reintentar(db, fila, superAdminId, ahora)) { /* 409 */ }
/// </code>
/// </example>
public sealed partial class ConsumidorReintentoAprovisionamiento(
    EmpresaDelTrabajo empresa, ImpuestosDb db, TimeProvider reloj, ILogger<ConsumidorReintentoAprovisionamiento> log)
{
    public const string ActorSuperAdmin = "SUPER_ADMIN";

    public async Task AplicarAsync(EventEnvelope envelope, JsonElement data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var pedido = ConsumidorCambioProducto.Leer<AprovisionamientoReintentar>(envelope, data);
        if (pedido.TenantId != envelope.TenantId)
            throw new SinReintentoAutomaticoException("La empresa de los datos no es la del sobre.");

        empresa.Fijar(envelope.TenantId);
        var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == envelope.TenantId, ct).ConfigureAwait(false);
        if (fila is null || !Reintentar(db, fila, pedido.SolicitadoPor, reloj.GetUtcNow()))
            SinEfecto(log, envelope.TenantId, fila?.Estado);
    }

    /// <summary>¿Admite «Reintentar» el estado?</summary>
    public static bool Reintentable(EstadoAprovisionamiento estado) =>
        estado is EstadoAprovisionamiento.Error or EstadoAprovisionamiento.Incierto or EstadoAprovisionamiento.RequiereRevisionNit;

    /// <summary>Aplica «Reintentar» a la fila (con seguimiento en <paramref name="db"/>, sin guardar); false si el estado no lo admite.</summary>
    internal static bool Reintentar(ImpuestosDb db, AprovisionamientoEmpresa fila, Guid? actor, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fila);
        if (!Reintentable(fila.Estado))
            return false;

        var desde = fila.Estado;
        var nueva = TransicionesAprovisionamiento.Aplicar(Aprovisionador.Situacion(fila), EventoAprovisionamiento.Reintentar).Nueva;
        fila.Estado = nueva.Estado!.Value;
        fila.EstadoPrevio = nueva.EstadoPrevio;
        fila.ErrorTipo = nueva.TipoError;
        fila.IntentosAutomaticos = nueva.IntentosAutomaticos;
        fila.FirmaConfirmada = nueva.FirmaConfirmada;
        fila.MotivoCodigo = null;
        // El Super Admin decidió reintentar: una marca de alta en el aire ya se resolvió como INCIERTO y no se vuelve a leer.
        fila.MotivoDetalle = null;
        fila.ProximoIntentoEn = null;
        fila.Version++;
        fila.ActualizadoEn = ahora;
        db.Add(new HistorialAprovisionamiento
        {
            TenantId = fila.TenantId,
            Desde = desde,
            Hacia = fila.Estado,
            ActorId = actor is { } id && id != Guid.Empty ? id : null,
            ActorTipo = ActorSuperAdmin,
            En = ahora,
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reintentar el aprovisionamiento de la empresa {Empresa} sin efecto (estado {Estado}).")]
    private static partial void SinEfecto(ILogger logger, Guid empresa, EstadoAprovisionamiento? estado);
}
