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
/// restantes". Por eso un <c>rechazado_intento</c> NO es terminal: se CUENTA el intento —deduplicando por su
/// <c>validadoAt</c> contra <see cref="ProcedureInstanceBiometricValidation.LastAttemptAt"/> (el mismo intento
/// llega por webhook + poll + reenvíos)— y solo se marca <c>rechazado</c> al agotar (<c>Attempts &gt;= MaxAttempts</c>).</para>
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

        switch ((status.Status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "aprobado":
                return await applier.ApplyAsync(
                    v, new IdentityValidationTerminalResult(true, status.Status, status.RawPayloadSanitized, status.Score), now, ct);

            case "rechazado_intento":
            {
                // Idempotencia: una validación ya terminal no se re-evalúa.
                if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado or BiometricEstados.Expirado)
                    return false;

                // Cuenta el intento deduplicando por su validadoAt contra la columna dedicada LastAttemptAt
                // (nunca la pisa un payload sin fecha): solo incrementa si es un intento NUEVO.
                var esNuevoIntento = !string.IsNullOrEmpty(status.AttemptAt)
                    && !string.Equals(status.AttemptAt, v.LastAttemptAt, StringComparison.Ordinal);
                if (esNuevoIntento)
                {
                    v.Attempts += 1;
                    v.LastAttemptAt = status.AttemptAt;
                }

                if (v.Attempts >= v.MaxAttempts)
                    // Intentos AGOTADOS → rechazo TERMINAL (publica el evento y habilita "Reintentar").
                    return await applier.ApplyAsync(
                        v, new IdentityValidationTerminalResult(false, status.Status, status.RawPayloadSanitized, status.Score), now, ct);

                // Aún quedan intentos → sigue EN_PROCESO (el cliente reintenta en su móvil). Solo si es un intento
                // NUEVO se persiste (conteo + payload con el motivo); un poll idéntico no re-escribe nada.
                if (esNuevoIntento)
                {
                    v.ProviderStatus = status.Status;
                    v.ProviderPayload = status.RawPayloadSanitized;
                    v.UpdatedAt = now;
                    return true;
                }
                return false;
            }

            // Compat: un "rechazado" ya terminal (fixtures u otros orígenes) se aplica directo.
            case "rechazado":
                return await applier.ApplyAsync(
                    v, new IdentityValidationTerminalResult(false, status.Status, status.RawPayloadSanitized, status.Score), now, ct);

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
                return false; // enviado / en_proceso / desconocido: aún pendiente.
        }
    }
}
