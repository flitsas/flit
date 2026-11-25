using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.Extensions.Logging;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// HU #14711 (plan §3.1 punto 1, AC-DC-03) — alta automática de la credencial al habilitar <c>diagnostico</c>: habilitar deja
/// la fila de <c>platform.tenant_product_provisioning(product_code='diagnostico')</c> en <c>PENDIENTE</c> y el trabajador de
/// core-identity la atiende con <see cref="ProcesarAsync"/>. Llama al alta aunque la empresa ya tenga llave y secreto (es
/// idempotente: sin cambios no escribe nada) y deja el estado:
/// <list type="bullet">
///   <item>éxito ⇒ <c>LISTO</c> (o <c>LISTO_SIN_WEBHOOK</c> con <c>SIN_WEBHOOK_SECRET</c> si no hay secreto);</item>
///   <item><c>PROVEEDOR_NO_DISPONIBLE</c> o <c>PROVEEDOR_NO_CONFIGURADO</c> ⇒ <c>PENDIENTE</c> con la espera escalonada de Impuestos
///   (1, 5, 15 y 60 min; <see cref="MaximoIntentosAutomaticos"/> intentos) y después <c>ERROR</c>;</item>
///   <item>NIT_* ⇒ <c>REQUIERE_REVISION_NIT</c> con la acción <c>CORREGIR_NIT</c>, sin reintento;</item>
///   <item><c>PRODUCTO_NO_HABILITADO</c> ⇒ <c>SUSPENDIDO</c>; los demás errores ⇒ <c>ERROR</c>, sin reintento.</item>
/// </list>
/// Una excepción inesperada (la base, por ejemplo) no deja estado: la fila sigue reservada y se reintenta al vencer la reserva.
/// </summary>
/// <example>
/// <code>
/// await automatica.ProcesarAsync(new PendienteAltaDiagnostico(tenantId, intentos), ct);
/// </code>
/// </example>
public sealed partial class AltaCredencialDiagnosticoAutomatica(
    ObtenerCredencialImpuestoDiagnosticoHandler handler,
    ITenantProductProvisioningStore store,
    TimeProvider clock,
    ILogger<AltaCredencialDiagnosticoAutomatica> log)
{
    /// <summary>Tope de intentos automáticos (el mismo de Impuestos, <c>TransicionesAprovisionamiento.MaximoIntentosAutomaticos</c>).</summary>
    public const int MaximoIntentosAutomaticos = 5;

    /// <summary>Filas que toma cada ronda.</summary>
    public const int Lote = 20;

    /// <summary>Segundos entre rondas por defecto.</summary>
    public const int IntervaloPorDefectoSegundos = 10;

    /// <summary>Cuánto queda reservada una fila mientras se atiende (la misma reserva de Impuestos).</summary>
    public static readonly TimeSpan Reserva = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    private readonly ObtenerCredencialImpuestoDiagnosticoHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly ITenantProductProvisioningStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>Espera antes del intento <paramref name="intento"/> (1 = el primer reintento).</summary>
    public static TimeSpan EsperaReintento(int intento) => Esperas[Math.Clamp(intento - 1, 0, Esperas.Length - 1)];

    public async Task ProcesarAsync(PendienteAltaDiagnostico pendiente, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        EstadoAltaDiagnostico estado;
        try
        {
            var resultado = await _handler.HandleAsync(pendiente.TenantId, changedBy: null, cancellationToken).ConfigureAwait(false);
            estado = EstadoTrasExito(pendiente.TenantId, resultado, _clock.GetUtcNow());
        }
        catch (AltaCredencialImpuestoException ex)
        {
            estado = EstadoTrasError(pendiente.TenantId, ex.Code, pendiente.Intentos, _clock.GetUtcNow());
        }

        await _store.RegistrarEstadoAltaDiagnosticoAsync(estado, cancellationToken).ConfigureAwait(false);
        Atendida(_log, pendiente.TenantId, estado.Estado, estado.MotivoCodigo, estado.Intentos);
    }

    /// <summary>Estado tras un alta que trajo credenciales.</summary>
    public static EstadoAltaDiagnostico EstadoTrasExito(Guid tenantId, ResultadoObtencionCredencialImpuesto resultado, DateTimeOffset en)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return resultado.HasWebhookSecret
            ? new EstadoAltaDiagnostico(tenantId, EstadosAltaDiagnostico.Listo, null, null, null, 0, null, en)
            : new EstadoAltaDiagnostico(
                tenantId, EstadosAltaDiagnostico.ListoSinWebhook, CodigosAltaCredencialImpuesto.SinWebhookSecret,
                CodigosAltaCredencialImpuesto.Literal(CodigosAltaCredencialImpuesto.SinWebhookSecret), null, 0, null, en);
    }

    /// <summary>Estado tras un rechazo con <paramref name="code"/>, con <paramref name="intentosPrevios"/> intentos fallidos.</summary>
    public static EstadoAltaDiagnostico EstadoTrasError(Guid tenantId, string code, int intentosPrevios, DateTimeOffset en)
    {
        var literal = CodigosAltaCredencialImpuesto.Literal(code);
        switch (code)
        {
            case CodigosAltaCredencialImpuesto.ProveedorNoDisponible or CodigosAltaCredencialImpuesto.ProveedorNoConfigurado:
                if (intentosPrevios < MaximoIntentosAutomaticos)
                {
                    var intento = intentosPrevios + 1;
                    return new EstadoAltaDiagnostico(
                        tenantId, EstadosAltaDiagnostico.Pendiente, code, literal, null, intento, en + EsperaReintento(intento), en);
                }

                return new EstadoAltaDiagnostico(tenantId, EstadosAltaDiagnostico.Error, code, literal, null, intentosPrevios, null, en);
            case CodigosAltaCredencialImpuesto.NitAusente or CodigosAltaCredencialImpuesto.NitAmbiguo or CodigosAltaCredencialImpuesto.NitIgualACabeza:
                return new EstadoAltaDiagnostico(
                    tenantId, EstadosAltaDiagnostico.RequiereRevisionNit, code, literal, EstadosAltaDiagnostico.AccionCorregirNit, intentosPrevios, null, en);
            case CodigosAltaCredencialImpuesto.ProductoNoHabilitado:
                return new EstadoAltaDiagnostico(tenantId, EstadosAltaDiagnostico.Suspendido, code, literal, null, intentosPrevios, null, en);
            default:
                return new EstadoAltaDiagnostico(tenantId, EstadosAltaDiagnostico.Error, code, literal, null, intentosPrevios, null, en);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Alta automática de la credencial del impuesto de la empresa {Empresa}: {Estado} ({Motivo}, intentos {Intentos}).")]
    private static partial void Atendida(ILogger logger, Guid empresa, string estado, string? motivo, int intentos);
}

/// <summary>HU #14711 — estados y acción del alta de <c>diagnostico</c> (mismo vocabulario que Impuestos, plan §3.1 de impuestos).</summary>
public static class EstadosAltaDiagnostico
{
    public const string Pendiente = "PENDIENTE";
    public const string Listo = "LISTO";
    public const string ListoSinWebhook = "LISTO_SIN_WEBHOOK";
    public const string Error = "ERROR";
    public const string RequiereRevisionNit = "REQUIERE_REVISION_NIT";
    public const string Suspendido = "SUSPENDIDO";
    public const string AccionCorregirNit = "CORREGIR_NIT";
}
