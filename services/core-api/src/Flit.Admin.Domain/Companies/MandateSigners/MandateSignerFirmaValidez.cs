using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13130 (ADR-0061) — si un mandatario Persona natural tiene FIRMA VÁLIDA: la vigencia propia del
/// mandatario (fija o por rango, HU #13129) y, cuando firma con biometría, una validación biométrica
/// APROBADA. Ajuste HU #13130b (decisión del PO, 01-oct): la identidad del mandatario NO se renueva mientras
/// su vigencia propia esté activa, así que la ventana de 30 días (<c>BiometricRules.VigenciaDias</c>) ya no
/// se le exige; sigue rigiendo el trámite y esta regla no la toca. Es el punto único que comparten la ficha
/// admin y el directorio de trámites.
/// </summary>
public static class MandateSignerFirmaValidez
{
    /// <summary>Vigencia propia terminada, o rango que aún no empieza.</summary>
    public const string MotivoFueraDeVigencia = "mandatario_fuera_de_vigencia";

    /// <summary>El mandatario está inactivo.</summary>
    public const string MotivoInactivo = "mandatario_inactivo";

    /// <summary>No tiene ninguna validación biométrica aprobada (una en curso no cuenta).</summary>
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
    /// <param name="identityStatus">Vocabulario de <see cref="AdminIdentityVigencia"/>. Para el mandatario,
    /// <c>valid</c> significa «tiene una validación biométrica aprobada» (sin ventana de 30 días).</param>
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

        // Una aprobación basta, sea cual sea su antigüedad (HU #13130b). Sin ella (en curso, rechazada, sin
        // validación) no hay firma.
        return identityStatus == AdminIdentityVigencia.Valid
            ? Resultado.Ok
            : new Resultado(false, MotivoSinValidacionAprobada);
    }
}
