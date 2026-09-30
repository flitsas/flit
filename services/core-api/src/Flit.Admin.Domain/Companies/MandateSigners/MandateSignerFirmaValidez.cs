using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13130 (ADR-0061) — si un mandatario Persona natural tiene FIRMA VÁLIDA. Las dos vigencias
/// CONVIVEN y se exigen ambas: la vigencia propia del mandatario (fija o por rango, HU #13129) y, cuando
/// firma con biometría, la validación biométrica vigente según la regla de 30 días del módulo de
/// identidad (<c>BiometricRules.VigenciaDias</c>, que esta regla NO modifica: solo consume su resultado ya
/// clasificado). Es el punto único que comparten la ficha admin y el directorio de trámites.
/// </summary>
public static class MandateSignerFirmaValidez
{
    /// <summary>Vigencia propia terminada, o rango que aún no empieza.</summary>
    public const string MotivoFueraDeVigencia = "mandatario_fuera_de_vigencia";

    /// <summary>El mandatario está inactivo.</summary>
    public const string MotivoInactivo = "mandatario_inactivo";

    /// <summary>Su validación biométrica aprobada superó la ventana de 30 días (o expiró).</summary>
    public const string MotivoBiometriaVencida = "biometria_vencida";

    /// <summary>No tiene ninguna validación biométrica aprobada (ni en curso).</summary>
    public const string MotivoSinValidacionAprobada = "sin_validacion_aprobada";

    /// <param name="Valida">Firma válida: se estampa la firma o el sello.</param>
    /// <param name="Motivo">Uno de los <c>Motivo*</c> cuando <paramref name="Valida"/> es falso.</param>
    public readonly record struct Resultado(bool Valida, string? Motivo)
    {
        public static readonly Resultado Ok = new(true, null);
    }

    /// <summary>
    /// Evalúa la firma de un mandatario. Solo aplica a la Persona natural: para los demás modelos devuelve
    /// <c>null</c> (no hay firma personal que validar).
    /// </summary>
    /// <param name="validityStatus">Estado de vigencia propia (<see cref="MandateValidityStatus"/>).</param>
    /// <param name="identityStatus">Vocabulario de <see cref="AdminIdentityVigencia"/> (valid, pending, expired, none).</param>
    /// <param name="hasVaultSignature">Tiene una firma del baúl vinculada (para inferir la forma en legados sin forma de firma).</param>
    public static Resultado? Evaluar(
        string? signerModel,
        string? signatureMethod,
        string? validityStatus,
        string? identityStatus,
        bool hasVaultSignature)
    {
        if (!string.IsNullOrEmpty(signerModel) && signerModel != MandateSignerModels.Natural)
        {
            return null;
        }

        // Vigencia propia primero: sin ella no hay firma, con biometría vigente o sin ella.
        switch (validityStatus)
        {
            case MandateValidityStatus.Inactivo:
                return new Resultado(false, MotivoInactivo);
            case MandateValidityStatus.Vencido or MandateValidityStatus.NoVigente:
                return new Resultado(false, MotivoFueraDeVigencia);
        }

        // Legado sin forma de firma: baúl si lo tiene vinculado, biometría en el resto.
        var metodo = signatureMethod
            ?? (hasVaultSignature ? MandateSignatureMethods.Baul : MandateSignatureMethods.Biometria);

        if (metodo == MandateSignatureMethods.Baul)
        {
            // La vigencia de la firma del baúl la resuelve la política del baúl al estampar.
            return Resultado.Ok;
        }

        return identityStatus switch
        {
            AdminIdentityVigencia.Valid => Resultado.Ok,
            AdminIdentityVigencia.Expired => new Resultado(false, MotivoBiometriaVencida),
            _ => new Resultado(false, MotivoSinValidacionAprobada),
        };
    }
}
