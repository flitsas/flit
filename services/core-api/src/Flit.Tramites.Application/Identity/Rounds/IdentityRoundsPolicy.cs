using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Flit.Tramites.Application.Identity.Rounds;

/// <summary>
/// Aplica el configurador GLOBAL de rondas (<see cref="IdentityRoundsConfig"/>). El conteo es POR FILA de validación:
/// <c>1 + ResendCount</c> (ver <see cref="IdentityRoundsConfig.RoundsUsed"/>).
/// <para>
/// Alerta: idempotente por (validación, ronda). La fila de auditoría se registra ANTES de enviar y es la guardia: un webhook repetido o la
/// reconciliación llegan a <see cref="IIdentityRoundsStore.TryRegisterAlertAsync"/> y no vuelven a enviar. Best-effort: un fallo de
/// correo o de base NUNCA rompe la aplicación del veredicto. Los logs llevan solo ids, ronda y resultado (nunca el contenido).
/// </para>
/// </summary>
public sealed partial class IdentityRoundsPolicy(
    IIdentityRoundsStore store,
    IIdentityRoundsAlertSender sender,
    ILogger<IdentityRoundsPolicy> logger) : IIdentityRoundsPolicy
{
    public async Task<bool> IsResendBlockedAsync(ProcedureInstanceBiometricValidation validation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var config = await store.GetConfigAsync(ct).ConfigureAwait(false);
        return !config.AllowsNewRound(validation);
    }

    public async Task NotifyRoundRejectedAsync(ProcedureInstanceBiometricValidation validation, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);

        // Solo un rechazo TERMINAL de la ronda (intentos agotados): uno aplicado con intentos disponibles aún puede revertirse.
        if (validation.Status != BiometricEstados.Rechazado || BiometricRules.EsRechazoKyverumConIntentosDisponibles(validation))
            return;

        var ronda = IdentityRoundsConfig.RoundsUsed(validation);
        try
        {
            var config = await store.GetConfigAsync(ct).ConfigureAwait(false);
            if (!config.ShouldAlert(validation))
                return;

            var destinatarios = config.CorreosAlerta;
            var registro = new IdentityRoundsAlertRecord(
                Guid.NewGuid(), validation.Id, validation.ProcedureInstanceId, ronda, config.DisparadorAlerta, destinatarios, now,
                destinatarios.Count == 0 ? IdentityRoundsAlertResults.SinDestinatarios : IdentityRoundsAlertResults.Pendiente);

            // Guardia de idempotencia: solo quien INSERTA la fila envía.
            if (!await store.TryRegisterAlertAsync(registro, ct).ConfigureAwait(false))
                return;
            if (destinatarios.Count == 0)
            {
                AlertaSinDestinatarios(validation.Id, ronda);
                return;
            }

            var mensaje = new IdentityRoundsAlertMessage(
                validation.TenantId,
                validation.Id,
                validation.ProcedureInstanceId,
                validation.ProcedureInstance?.ReferenceNumber,
                validation.Name,
                validation.DocumentType,
                validation.DocumentNumber,
                IdentityRejectionReasonText.For(validation) ?? IdentityRejectionReasonText.Generico,
                ronda,
                config.RondasMaximas,
                config.DisparadorAlerta,
                now);

            var entrega = await sender.SendAsync(mensaje, destinatarios, ct).ConfigureAwait(false);
            var resultado = entrega switch
            {
                { Enviados: > 0, Fallidos: 0 } => IdentityRoundsAlertResults.Enviado,
                { Enviados: > 0 } => IdentityRoundsAlertResults.Parcial,
                _ => IdentityRoundsAlertResults.Fallido,
            };
            await store.CompleteAlertAsync(registro.Id, resultado, ct).ConfigureAwait(false);
            AlertaProcesada(validation.Id, ronda, resultado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Solo el tipo: el mensaje de la excepción podría arrastrar correos o datos de la persona.
            AlertaFallo(validation.Id, ronda, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rondas de identidad: ronda {Ronda} de la validación {ValidationId} sin destinatarios configurados; solo se audita.")]
    private partial void AlertaSinDestinatarios(Guid validationId, int ronda);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rondas de identidad: alerta de la ronda {Ronda} de la validación {ValidationId} procesada ({Resultado}).")]
    private partial void AlertaProcesada(Guid validationId, int ronda, string resultado);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rondas de identidad: no se pudo procesar la alerta de la ronda {Ronda} de la validación {ValidationId} ({ErrorType}).")]
    private partial void AlertaFallo(Guid validationId, int ronda, string errorType);
}
