using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Identity;
using Flit.Tramites.Application.Identity;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Adaptador del proveedor de identidad administrativa (HU #10907, ADR-0034) que REUTILIZA el cliente
/// Kyverum (<see cref="IKyverumVerifyClient"/>) de forma DESACOPLADA de un trámite.
///
/// <para><b>Riesgo R2 (ADR-0034) — resuelto:</b> Kyverum NO exige un procedure instance real. El
/// <c>externalRef</c> del create es una referencia OPACA (aquí, el id del sujeto — p.ej. el representante
/// legal) y la correlación de la reconciliación/webhook se hace por el id de NUESTRA validación incrustado
/// en la URL de callback. Por eso la validación de identidad puede iniciarse fuera de todo trámite: se pasa
/// <c>SubjectRef</c> como <c>externalRef</c> (el parámetro se llama <c>ProcedureInstanceId</c> por el
/// contrato del cliente, pero Kyverum lo trata como texto opaco). El secreto del webhook se CIFRA aquí
/// (Data Protection) antes de devolverlo, de modo que la capa de aplicación nunca lo maneja en claro.</para>
/// </summary>
internal sealed class KyverumAdminIdentityValidationProvider(
    IKyverumVerifyClient kyverum,
    IWebhookSecretProtector secretProtector) : IAdminIdentityValidationProvider
{
    public string Name => AdminIdentityProviders.Kyverum;

    public async Task<AdminIdentityStartResult> StartAsync(
        AdminIdentityStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // R2: SubjectRef viaja como externalRef opaco (desacoplado de un trámite); ValidationId es la
            // correlación por URL de callback. Parte=null (sujeto único, sin comprador/vendedor).
            var r = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    request.SubjectRef,
                    request.ValidationId,
                    Parte: null,
                    request.Name,
                    request.DocumentType,
                    request.DocumentNumber,
                    request.Email),
                ct).ConfigureAwait(false);

            // El secreto viaja en claro solo en memoria: se CIFRA antes de salir del adaptador.
            var secretEncrypted = string.IsNullOrEmpty(r.WebhookSecret)
                ? null
                : secretProtector.Protect(r.WebhookSecret);

            return new AdminIdentityStartResult(
                r.VerificationId, r.CaptureUrl, secretEncrypted, r.ProviderStatus, r.RawPayloadSanitized);
        }
        catch (KyverumVerifyException ex)
        {
            throw new AdminIdentityProviderException(ex.Message, ex.Transient);
        }
    }

    public async Task<AdminIdentityStatusResult?> GetStatusAsync(string verificationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationId);

        try
        {
            var status = await kyverum.GetStatusAsync(verificationId, parte: null, ct).ConfigureAwait(false);
            if (status is null)
            {
                return null;
            }

            // El cliente ya normaliza el veredicto: `aprobado` (result cerrado aprobado), `rechazado`
            // (terminal: Kyverum CERRÓ la validación, result.closedAt presente — Bug #11503) o
            // `rechazado_intento` (un INTENTO falló SIN señal de cierre; aún quedan reintentos, HU #11504)
            // — en_proceso/enviado se mapean como "sigue en proceso" (ambos false). La serie/hash del
            // certificado (firmaSerie) es el CertificateHash de la identidad aprobada (HU #10488).
            // `AttemptAt` (validadoAt del intento) viaja como clave de dedup para que el reconciliador
            // admin cuente el intento una sola vez por reintento real (ver AdminIdentityStatusResult).
            var approved = string.Equals(status.Status, "aprobado", StringComparison.OrdinalIgnoreCase);
            var rejected = string.Equals(status.Status, "rechazado", StringComparison.OrdinalIgnoreCase);
            var rejectedAttempt = string.Equals(status.Status, "rechazado_intento", StringComparison.OrdinalIgnoreCase);

            return new AdminIdentityStatusResult(
                approved, rejected, status.Status, status.FirmaSerie, status.RawPayloadSanitized,
                RejectedAttempt: rejectedAttempt, AttemptKey: status.AttemptAt);
        }
        catch (KyverumVerifyException ex)
        {
            throw new AdminIdentityProviderException(ex.Message, ex.Transient);
        }
    }
}
