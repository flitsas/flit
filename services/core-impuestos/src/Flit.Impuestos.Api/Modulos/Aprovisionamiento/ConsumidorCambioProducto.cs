using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>Datos de <c>impuestos.producto.cambiado</c> v1 (<c>impuestos-events.v1.yaml</c>, M03).</summary>
public sealed record ProductoCambiado(Guid TenantId, string? ProductCode, bool Enabled, DateTimeOffset ChangedAt);

/// <summary>
/// Consumidor de la cola <see cref="Cola"/> (exchange <c>flit.impuestos</c>, solo trabajos de los productores
/// <see cref="Productores"/>), que reciben los dos trabajos de la plataforma (D-ACT, plan §1.1 decisión E2). El SDK ya abrió la
/// transacción y anota la bandeja en el mismo guardado, así que una re-entrega del mismo <c>eventId</c> no repite el efecto.
/// <list type="bullet">
///   <item><see cref="TipoCambio"/> v1 <c>{tenantId, productCode, enabled, changedAt}</c>: fija la empresa del sobre
///   (<see cref="EmpresaDelTrabajo"/>) y aplica la máquina de estados: encender sin fila crea <c>PENDIENTE</c> (versión 1) para
///   que <see cref="TrabajadorReintentoAlta"/> lo evalúe; apagar suspende guardando el estado previo (D-DESACT, sin tocar la
///   credencial ni el API); encender una suspendida recupera el previo listo sin alta o vuelve a <c>PENDIENTE</c>. Tolera el
///   apagado sin fila y el encendido de una que ya está encendida (cambio solo de notas): se confirman sin efecto.</item>
///   <item><see cref="TipoReintentar"/>: lo atiende <see cref="ConsumidorReintentoAprovisionamiento"/>.</item>
/// </list>
/// Un sobre sin empresa, con otra empresa en los datos o de una versión que no conoce va a la DLQ sin reintentos
/// (<see cref="SinReintentoAutomaticoException"/>); otro producto u otro tipo se confirma sin efecto. Cada transición sube la
/// versión y deja historial con actor <c>PLATAFORMA</c>; el reporte a la plataforma lo hace la ronda del trabajador.
/// </summary>
/// <example>
/// <code>
/// services.AddFlitConsumer&lt;ImpuestosDb, ConsumidorCambioProducto, JsonElement&gt;(configuration, ConsumidorCambioProducto.Cola,
///     ConsumidorCambioProducto.Exchange, [ConsumidorCambioProducto.TipoCambio, ConsumidorCambioProducto.TipoReintentar]);
/// </code>
/// </example>
public sealed partial class ConsumidorCambioProducto(
    EmpresaDelTrabajo empresa,
    ImpuestosDb db,
    ConsumidorReintentoAprovisionamiento reintento,
    TimeProvider reloj,
    ILogger<ConsumidorCambioProducto> log) : IEventConsumer<JsonElement>
{
    public const string Cola = "impuestos.plataforma";

    /// <summary>Dueño del exchange (<c>flit.impuestos</c>): los trabajos van al exchange del servicio que los atiende.</summary>
    public const string Exchange = "impuestos";

    public const string TipoCambio = "impuestos.producto.cambiado";
    public const string TipoReintentar = "impuestos.aprovisionamiento.reintentar";
    public const string Producto = "impuestos";
    public const string ActorPlataforma = "PLATAFORMA";

    /// <summary>core-api (<c>tramites</c>) y core-identity (<c>plataforma</c>), los dos hosts del PUT de productos.</summary>
    public static IReadOnlyList<string> Productores { get; } = ["tramites", "plataforma"];

    public async Task HandleAsync(EventEnvelope envelope, JsonElement data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.Equals(envelope.Type, TipoReintentar, StringComparison.Ordinal))
        {
            await reintento.AplicarAsync(envelope, data, ct).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(envelope.Type, TipoCambio, StringComparison.Ordinal))
        {
            Ignorado(log, envelope.EventId, envelope.Type);
            return;
        }

        var cambio = Leer<ProductoCambiado>(envelope, data);
        if (cambio.TenantId != envelope.TenantId)
            throw new SinReintentoAutomaticoException("La empresa de los datos no es la del sobre.");
        if (!string.Equals(cambio.ProductCode, Producto, StringComparison.Ordinal))
        {
            Ignorado(log, envelope.EventId, envelope.Type);
            return;
        }

        empresa.Fijar(envelope.TenantId);
        var ahora = reloj.GetUtcNow();
        var fila = await db.Set<AprovisionamientoEmpresa>().SingleOrDefaultAsync(a => a.TenantId == envelope.TenantId, ct).ConfigureAwait(false);
        if (fila is null)
        {
            if (!cambio.Enabled)
            {
                SinEfecto(log, envelope.TenantId, "apagado sin fila");
                return;
            }

            db.Add(new AprovisionamientoEmpresa
            {
                TenantId = envelope.TenantId,
                Estado = EstadoAprovisionamiento.Pendiente,
                Version = 1,
                UltimoCambioPlataformaId = envelope.EventId,
                CreadoEn = ahora,
                ActualizadoEn = ahora,
            });
            db.Add(Historial(envelope.TenantId, null, EstadoAprovisionamiento.Pendiente, null, ahora));
            Aplicado(log, envelope.TenantId, null, EstadoAprovisionamiento.Pendiente);
            return;
        }

        var encendido = fila.Estado != EstadoAprovisionamiento.Suspendido;
        if (cambio.Enabled == encendido || fila.UltimoCambioPlataformaId == envelope.EventId)
        {
            SinEfecto(log, envelope.TenantId, cambio.Enabled ? "ya estaba encendido" : "ya estaba apagado");
            return;
        }

        var desde = fila.Estado;
        var nueva = TransicionesAprovisionamiento.Aplicar(
            Aprovisionador.Situacion(fila),
            cambio.Enabled ? EventoAprovisionamiento.ProductoHabilitado : EventoAprovisionamiento.ProductoDeshabilitado).Nueva;
        fila.Estado = nueva.Estado!.Value;
        fila.EstadoPrevio = nueva.EstadoPrevio;
        fila.ErrorTipo = nueva.TipoError;
        fila.IntentosAutomaticos = nueva.IntentosAutomaticos;
        fila.FirmaConfirmada = nueva.FirmaConfirmada;
        fila.MotivoCodigo = MotivosAprovisionamiento.DelEstado(fila.Estado);
        fila.ProximoIntentoEn = null;
        fila.UltimoCambioPlataformaId = envelope.EventId;
        fila.Version++;
        fila.ActualizadoEn = ahora;
        db.Add(Historial(envelope.TenantId, desde, fila.Estado, fila.MotivoCodigo, ahora));
        Aplicado(log, envelope.TenantId, desde, fila.Estado);
    }

    /// <summary>Los datos del trabajo en su versión 1; si no se pueden leer, a la DLQ sin reintentos.</summary>
    internal static T Leer<T>(EventEnvelope envelope, JsonElement data)
        where T : class
    {
        if (envelope.Version != 1)
            throw new SinReintentoAutomaticoException($"Versión {envelope.Version} de {envelope.Type} no soportada (solo la 1).");
        if (envelope.TenantId == Guid.Empty)
            throw new SinReintentoAutomaticoException("El sobre no trae la empresa.");
        try
        {
            return data.Deserialize<T>(EventEnvelope.JsonOptions) ?? throw new SinReintentoAutomaticoException($"{envelope.Type} sin datos.");
        }
        catch (JsonException)
        {
            throw new SinReintentoAutomaticoException($"Los datos de {envelope.Type} no tienen la forma del contrato.");
        }
    }

    private static HistorialAprovisionamiento Historial(Guid empresa, EstadoAprovisionamiento? desde, EstadoAprovisionamiento hacia, string? motivo, DateTimeOffset en) => new()
    {
        TenantId = empresa,
        Desde = desde,
        Hacia = hacia,
        MotivoCodigo = motivo,
        ActorTipo = ActorPlataforma,
        En = en,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Cambio de producto de la empresa {Empresa}: {Desde} → {Hacia}.")]
    private static partial void Aplicado(ILogger logger, Guid empresa, EstadoAprovisionamiento? desde, EstadoAprovisionamiento hacia);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cambio de producto de la empresa {Empresa} sin efecto: {Motivo}.")]
    private static partial void SinEfecto(ILogger logger, Guid empresa, string motivo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trabajo {EventId} ({Tipo}) sin efecto para Impuestos.")]
    private static partial void Ignorado(ILogger logger, Guid eventId, string tipo);
}
