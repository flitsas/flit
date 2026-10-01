using Flit.Tramites.Application.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Qué pasó con el correo de validación de identidad tras asegurar la identidad de una parte.</summary>
public static class IdentityNotificationOutcomes
{
    /// <summary>No hacía falta: la identidad ya está cubierta (vigente, reusada, baúl) o la parte no valida.</summary>
    public const string NoRequerida = "no_requerida";

    /// <summary>Se inició la validación con el proveedor configurado (Kyverum envía el correo).</summary>
    public const string Enviada = "enviada";

    /// <summary>Ya hay una validación en curso para la parte: no se duplica la sesión (<c>biometria_activa</c>).</summary>
    public const string YaEnCurso = "ya_en_curso";

    /// <summary>Hacía falta y no se pudo iniciar; <see cref="EnsureIdentityAndNotifyResult.NotificacionError"/> dice por qué.</summary>
    public const string Fallida = "fallida";
}

/// <summary>
/// Resultado de <see cref="EnsureIdentityAndNotifyHandler"/>: el desenlace de <see cref="EnsureIdentityHandler"/>
/// (<see cref="EnsureIdentityOutcomes"/>) y lo que pasó con el correo (<see cref="IdentityNotificationOutcomes"/>).
/// <see cref="NotificacionError"/> es un código del handler de inicio (<c>datos_incompletos</c>,
/// <c>proveedor_no_disponible</c>, <c>not_draft</c>, <c>exception</c>…), nunca un dato personal.
/// </summary>
public sealed record EnsureIdentityAndNotifyResult(
    string Outcome,
    Guid? ValidationId,
    string Notificacion,
    string? NotificacionError = null);

/// <summary>
/// Bug #13194 (punto 4) — <b>EnsureIdentityAndNotify(instancia, parte)</b>: asegura la identidad de una parte
/// y, si no está cubierta, dispara el correo de validación. Es el método reutilizable para cualquier punto
/// del ciclo de vida que deba garantizar la firma sin pasar por el asistente: la materialización ICT, el
/// gate de envío al organismo (cuando la validación aprobada o el baúl vencieron) o un reintento manual.
///
/// <list type="number">
///   <item><see cref="EnsureIdentityHandler"/> decide con la precedencia de siempre: validación propia
///   vigente → en curso → baúl del representante (PJ, compañía con baúl activo) → identidad vigente de la
///   persona en otro trámite del MISMO tenant → <c>requiere_validacion</c>.</item>
///   <item>Solo con <c>requiere_validacion</c> se inicia la validación con el proveedor configurado
///   (Kyverum real, o la simulación en entornos mock), igual que hacían el asistente y el ICT.</item>
///   <item>Idempotente: una validación en curso responde <see cref="IdentityNotificationOutcomes.YaEnCurso"/>
///   (el handler de Kyverum la protege con <c>biometria_activa</c>), así que llamarlo dos veces no
///   duplica sesiones ni correos.</item>
/// </list>
///
/// <para>Admite los trámites radicados pendientes (<see cref="Flit.Tramites.Domain.Tramites.Estados.TramiteFirmaPendiente"/>):
/// una identidad que vence entre la radicación y el envío al organismo se renueva sin devolver el trámite
/// a borrador. Los fallos del proveedor NO se tragan: vuelven en el resultado y se registran sin PII.</para>
/// </summary>
public sealed class EnsureIdentityAndNotifyHandler(
    EnsureIdentityHandler ensureHandler,
    IniciarKyverumVerifyHandler kyverumHandler,
    SimularBiometriaHandler simularHandler,
    BiometricsProviderOptions providerOptions,
    ILogger<EnsureIdentityAndNotifyHandler>? logger = null)
{
    private const string BiometriaActiva = "biometria_activa";
    private readonly ILogger _logger = logger ?? NullLogger<EnsureIdentityAndNotifyHandler>.Instance;

    /// <param name="documento">
    /// Documento del sujeto cuando el rol tiene varios actores (ADR-0053); con uno solo es irrelevante.
    /// </param>
    public async Task<(EnsureIdentityAndNotifyResult? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string parte,
        string? documento = null,
        CancellationToken ct = default)
    {
        var (ensured, ensureError) = await ensureHandler.HandleAsync(instanceId, tenantId, parte, documento, ct);
        if (ensureError is not null || ensured is null)
            return (null, ensureError ?? "ensure_null");

        if (ensured.Outcome == EnsureIdentityOutcomes.EnProceso)
            return (new EnsureIdentityAndNotifyResult(ensured.Outcome, ensured.ValidationId, IdentityNotificationOutcomes.YaEnCurso), null);

        if (ensured.Outcome != EnsureIdentityOutcomes.RequiereValidacion)
            return (new EnsureIdentityAndNotifyResult(ensured.Outcome, ensured.ValidationId, IdentityNotificationOutcomes.NoRequerida), null);

        string? startError;
        Guid? validationId = null;
        try
        {
            if (providerOptions.IsKyverum)
            {
                var (started, kyverumError, _) = await kyverumHandler.HandleParaRadicadoPendienteAsync(
                    instanceId,
                    tenantId,
                    new IniciarBiometriaInput(parte, string.Empty, string.Empty, documento ?? string.Empty, string.Empty),
                    ct);
                startError = kyverumError;
                validationId = started?.Validation.Id;
            }
            else
            {
                var (simulated, simulateError) = await simularHandler.HandleAsync(instanceId, tenantId, parte, documento, ct);
                startError = simulateError;
                validationId = simulated?.Id;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Sin PII: solo ids, parte y el tipo de excepción.
            EnsureIdentityAndNotifyLog.Exception(_logger, ex.GetType().Name, instanceId, parte);
            return (new EnsureIdentityAndNotifyResult(
                ensured.Outcome, null, IdentityNotificationOutcomes.Fallida, "exception"), null);
        }

        if (startError is null)
            return (new EnsureIdentityAndNotifyResult(ensured.Outcome, validationId, IdentityNotificationOutcomes.Enviada), null);

        if (string.Equals(startError, BiometriaActiva, StringComparison.Ordinal))
            return (new EnsureIdentityAndNotifyResult(ensured.Outcome, null, IdentityNotificationOutcomes.YaEnCurso), null);

        EnsureIdentityAndNotifyLog.NotStarted(_logger, startError, instanceId, parte);
        return (new EnsureIdentityAndNotifyResult(
            ensured.Outcome, null, IdentityNotificationOutcomes.Fallida, startError), null);
    }
}

/// <summary>Logs sin PII (solo ids, parte y códigos) de <see cref="EnsureIdentityAndNotifyHandler"/>.</summary>
internal static partial class EnsureIdentityAndNotifyLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Validación de identidad no iniciada: {ErrorCode}. Instancia {InstanceId}, parte {Parte}.")]
    public static partial void NotStarted(ILogger logger, string errorCode, Guid instanceId, string parte);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Validación de identidad no iniciada por excepción {ExceptionType}. Instancia {InstanceId}, parte {Parte}.")]
    public static partial void Exception(ILogger logger, string exceptionType, Guid instanceId, string parte);
}
