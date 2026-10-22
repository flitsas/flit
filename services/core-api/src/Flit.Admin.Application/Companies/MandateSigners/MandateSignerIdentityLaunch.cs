using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// HU #13246 (Feature #13245, Épica #13090) — reglas y disparo compartidos de la validación de identidad PROPIA del
/// mandatario: la lanzan el alta (compañía y hub OT), la edición (cambio de documento, paso de baúl a biometría) y el
/// reenvío. Solo la Persona natural con forma de firma biometría la necesita; baúl, Persona jurídica y Formato en blanco
/// no. Reactivar NO la dispara.
/// </summary>
internal static class MandateSignerIdentityLaunch
{
    /// <summary>Forma de firma efectiva: en un legado sin forma, baúl si tiene firma vinculada y biometría si no.</summary>
    public static string? EffectiveMethod(MandateSignerItem signer) =>
        signer.SignerModel != MandateSignerModels.Natural
            ? null
            : signer.SignatureMethod
                ?? (signer.SignatureVaultId is not null ? MandateSignatureMethods.Baul : MandateSignatureMethods.Biometria);

    /// <summary>Mismo documento: tipo y número iguales tras Trim y mayúsculas (regla canónica del módulo Identidad).</summary>
    public static bool SameDocument(string? leftType, string? leftNumber, string? rightType, string? rightNumber) =>
        string.Equals(Norm(leftType), Norm(rightType), StringComparison.Ordinal)
        && string.Equals(Norm(leftNumber), Norm(rightNumber), StringComparison.Ordinal);

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static bool RequiresValidation(string? signerModel, string? signatureMethod) =>
        signerModel == MandateSignerModels.Natural && signatureMethod == MandateSignatureMethods.Biometria;

    /// <summary>
    /// Lanza la validación del mandatario. NUNCA lanza excepción ni hace fallar la operación que la origina: el
    /// mandatario ya está guardado y el desenlace viaja al cliente.
    /// </summary>
    public static async Task<MandateSignerIdentityOutcome> TryLaunchAsync(
        IMandateSignerIdentityLauncher? launcher,
        Guid mandateSignerId,
        Guid tenantId,
        string documentType,
        string? documentNumber,
        string fullName,
        string? email,
        CancellationToken cancellationToken)
    {
        if (launcher is null || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(documentNumber)
            || string.IsNullOrWhiteSpace(email))
        {
            return MandateSignerIdentityOutcome.NotAttempted;
        }

        try
        {
            var result = await launcher
                .LaunchAsync(
                    new MandateSignerIdentityLaunchRequest(
                        mandateSignerId, tenantId, documentType, documentNumber.Trim(), fullName, email.Trim()),
                    cancellationToken)
                .ConfigureAwait(false);
            return Map(result.Outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MandateSignerIdentityOutcome.Failed;
        }
    }

    public static MandateSignerIdentityOutcome Map(MandateSignerIdentityLaunchOutcome outcome) => outcome switch
    {
        MandateSignerIdentityLaunchOutcome.Sent or MandateSignerIdentityLaunchOutcome.AlreadyInFlight
            => MandateSignerIdentityOutcome.Sent,
        MandateSignerIdentityLaunchOutcome.Queued => MandateSignerIdentityOutcome.Queued,
        _ => MandateSignerIdentityOutcome.Failed,
    };
}
