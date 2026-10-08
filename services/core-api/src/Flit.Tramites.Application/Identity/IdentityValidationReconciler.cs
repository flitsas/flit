using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Mapea el estado consultado a Kyverum (<see cref="KyverumVerifyStatus"/>) al estado FLIT y lo aplica a la
/// validación, reusando <see cref="IdentityValidationResultApplier"/> para el veredicto terminal (misma lógica
/// que el webhook). Lo comparten el endpoint on-demand, el worker periódico y el respaldo del webhook. NO
/// persiste: devuelve si hubo cambios y el caller hace <c>SaveChanges</c>.
///
/// <para><b>Reintentos de Kyverum:</b> Kyverum permite N intentos dentro de UNA validación y reporta
/// <c>result</c>/rechazado tras CADA intento fallido (aunque queden reintentos); la API NO expone "intentos
/// restantes". El conteo de intentos es AUTORITATIVO por webhook (<see cref="Flit.Tramites.Application.UseCases.ProcedureInstances.KyverumWebhookHandler"/>,
/// un <c>validation.rejected</c> = un intento, deduplicado por el cuerpo). Este reconciliador —que corre por
/// poll del worker y por respaldo— ya NO cuenta: eso evita el doble-conteo webhook+poll que inflaba los
/// intentos. Ante <c>rechazado_intento</c> solo (1) TERMINALIZA en rechazado si el conteo del webhook ya agotó
/// los intentos (<c>Attempts &gt;= MaxAttempts</c>), o (2) refresca el motivo del último intento; nunca incrementa.</para>
/// <para><b>Bug #11503:</b> antes, <c>KyverumVerifyClient.GetStatusAsync</c> mapeaba el status top-level
/// <c>rechazado</c> a terminal de forma incondicional (asumiendo que Kyverum solo lo emitía al cerrar), lo
/// que congelaba la fila en <c>rechazado</c> tras el PRIMER intento fallido de la ruta de consulta (poll del
/// worker, botón "Actualizar estado", respaldo del webhook) aunque quedaran reintentos y el ciudadano
/// aprobara después. El cliente ahora discrimina con <c>result.closedAt</c> (única señal fiable de cierre):
/// un rechazo SIN esa señal llega aquí como <c>rechazado_intento</c> (no terminal); un rechazo CON esa señal
/// llega como <c>rechazado</c>.</para>
/// <para><b>Rechazo antes del tercer intento:</b> <c>result.closedAt</c> tampoco resultó fiable: Kyverum lo trae
/// también tras un intento intermedio, y la fila quedaba en <c>rechazado</c> con 1 de 3 intentos aunque la persona
/// aprobara en el siguiente. Ahora el rechazo solo es terminal cuando el conteo de intentos se agotó, venga como
/// <c>rechazado</c> o como <c>rechazado_intento</c>; mientras queden intentos se sigue consultando. Y el aprobado de
/// Kyverum gana sobre un rechazo aplicado antes de agotar los intentos
/// (<see cref="BiometricRules.EsRechazoKyverumConIntentosDisponibles"/>).</para>
/// </summary>
public static class IdentityValidationReconciler
{
    public static async Task<bool> ApplyStatusAsync(
        IdentityValidationResultApplier applier,
        ProcedureInstanceBiometricValidation v,
        KyverumVerifyStatus status,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(status);

        // Bug #13055 — trámite anulado o revocado: ni el refresco de "rechazado_intento" ni la expiración
        // (que no pasan por el applier) tocan la validación.
        if (v.CongeladaPorTramite)
            return false;

        switch ((status.Status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "aprobado":
                return await applier.ApplyAsync(
                    v, new IdentityValidationTerminalResult(true, status.Status, status.RawPayloadSanitized, status.Score, status.FirmaSerie), now, ct);

            // El rechazo solo es terminal con los intentos agotados, traiga o no la señal de cierre de Kyverum.
            case "rechazado_intento":
            case "rechazado":
                {
                    // Idempotencia: una validación ya terminal no se re-evalúa.
                    if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado or BiometricEstados.Expirado)
                        return false;

                    // El conteo lo lleva el webhook (autoritativo). Aquí NO se incrementa: si el webhook ya agotó los
                    // intentos, se terminaliza en rechazado; si aún quedan, solo se refresca el motivo del último intento.
                    if (v.Attempts >= v.MaxAttempts)
                        // Intentos AGOTADOS → rechazo TERMINAL (publica el evento y habilita "Reintentar").
                        return await applier.ApplyAsync(
                            v, new IdentityValidationTerminalResult(false, status.Status, status.RawPayloadSanitized, status.Score), now, ct);

                    // Aún quedan intentos → sigue EN_PROCESO (el cliente reintenta en su móvil). Se refresca el
                    // payload (motivo del último intento para la UI) solo si CAMBIÓ; un poll idéntico no re-escribe.
                    if (!string.Equals(v.ProviderPayload, status.RawPayloadSanitized, StringComparison.Ordinal))
                    {
                        v.ProviderStatus = status.Status;
                        v.ProviderPayload = status.RawPayloadSanitized;
                        v.UpdatedAt = now;
                        return true;
                    }
                    return false;
                }

            case "expirado":
                // Expiró en Kyverum sin resolución: se marca expirado (no es un resultado → sin evento Completed).
                if (v.Status is BiometricEstados.Expirado or BiometricEstados.Aprobado or BiometricEstados.Rechazado)
                    return false;
                v.Status = BiometricEstados.Expirado;
                v.ProviderStatus = status.Status;
                v.ProviderPayload = status.RawPayloadSanitized;
                v.UpdatedAt = now;
                return true;

            default:
                // Worker/poll: si el webhook ya agotó Attempts pero Kyverum aún reporta en_proceso/otro,
                // terminalizar para no dejar prevalidaciones/trámites colgados en "en proceso".
                if (v.Status is not (BiometricEstados.Aprobado or BiometricEstados.Rechazado or BiometricEstados.Expirado)
                    && v.MaxAttempts > 0
                    && v.Attempts >= v.MaxAttempts)
                {
                    return await applier.ApplyAsync(
                        v,
                        new IdentityValidationTerminalResult(
                            false, status.Status, status.RawPayloadSanitized, status.Score),
                        now,
                        ct);
                }

                return false; // enviado / en_proceso / desconocido: aún pendiente.
        }
    }
}
