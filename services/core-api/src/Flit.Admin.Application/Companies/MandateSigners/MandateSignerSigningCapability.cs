using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// HU #11715 — un mandatario no puede quedar habilitado en un organismo si no está en condiciones de
/// firmar el mandato ante él. La comprobación va aquí, al PARAMETRIZAR, y no al emitir: cuando el
/// trámite llega, la firma ya está garantizada y no hay nada que reintentar.
///
/// <para><b>La condición replica la precedencia de <c>MandatarioFirmaResolver</c></b> —imagen del baúl
/// → sello de la validación de identidad vigente → línea en blanco—. Sin ninguna de las dos, el
/// contrato salía con la línea de guiones bajos sin que nadie lo advirtiera.</para>
///
/// <para><b>Excepción: la firma física.</b> Un organismo marcado en
/// <c>PhysicalSignatureOfficeIds</c> no exige baúl ni identidad al parametrizar (se puede dejar
/// línea en blanco). Si el mandatario ya tiene imagen o sello, el contrato las estampa igual:
/// el modelo a mano no las oculta.</para>
///
/// <para><b>La identidad recién enviada cuenta.</b> Un mandatario nuevo no tiene identidad vigente
/// —se le envía al registrarlo, con su correo—, así que exigir <c>valid</c> haría imposible dar de
/// alta a nadie que no tuviera ya firma en el baúl. Basta con que la validación esté en camino:
/// <c>pending</c>, o un correo al que mandarla.</para>
/// </summary>
public static class MandateSignerSigningCapability
{
    public const string Field = "transitOfficeIds";

    public const string SinMedioDeFirmaMessage =
        "El mandatario no está en condiciones de firmar en los organismos indicados: no tiene firma en "
        + "el baúl ni validación de identidad. Captúrale la firma, registra un correo para enviarle la "
        + "validación de identidad, o marca esos organismos como de firma física.";

    /// <summary>
    /// Estado de identidad que cuenta como resuelta o en curso. <c>expired</c> NO cuenta: una
    /// validación vencida no estampa sello, y renovarla es una acción explícita del gestor.
    /// </summary>
    private static readonly string[] IdentidadResueltaOEnCurso = ["valid", "pending"];

    /// <summary>
    /// Organismos de <paramref name="offices"/> en los que el mandatario quedaría sin poder firmar.
    /// Vacío ⇒ se puede habilitar en todos.
    /// </summary>
    /// <param name="offices">Organismos que el formulario quiere dejar habilitados.</param>
    /// <param name="physicalSignatureOfficeIds">Los que se firman a mano (exentos).</param>
    /// <param name="signatureVaultId">Firma del baúl elegida en la petición.</param>
    /// <param name="email">Correo al que se enviaría la validación de identidad.</param>
    /// <param name="existente">
    /// Mandatario ya registrado, en la edición. <c>null</c> en el alta. Aporta la firma y la identidad
    /// que ya tiene, para no exigir que se vuelvan a mandar en cada guardado.
    /// </param>
    public static IReadOnlyList<Guid> OrganismosSinMedioDeFirma(
        IReadOnlyList<Guid> offices,
        IReadOnlyList<Guid>? physicalSignatureOfficeIds,
        Guid? signatureVaultId,
        string? email,
        MandateSignerItem? existente = null)
    {
        ArgumentNullException.ThrowIfNull(offices);

        if (PuedeFirmarElectronicamente(signatureVaultId, email, existente))
        {
            return [];
        }

        var fisicos = physicalSignatureOfficeIds is null
            ? []
            : new HashSet<Guid>(physicalSignatureOfficeIds);

        return [.. offices.Where(o => !fisicos.Contains(o))];
    }

    /// <summary>
    /// Error 422 listo para devolver, o <c>null</c> si el mandatario puede firmar en todos los
    /// organismos indicados.
    /// </summary>
    public static MandateSignerValidationError? Validate(
        IReadOnlyList<Guid> offices,
        IReadOnlyList<Guid>? physicalSignatureOfficeIds,
        Guid? signatureVaultId,
        string? email,
        MandateSignerItem? existente = null)
    {
        var sinFirma = OrganismosSinMedioDeFirma(
            offices, physicalSignatureOfficeIds, signatureVaultId, email, existente);

        return sinFirma.Count == 0
            ? null
            : new MandateSignerValidationError(Field, SinMedioDeFirmaMessage, null);
    }

    private static bool PuedeFirmarElectronicamente(
        Guid? signatureVaultId,
        string? email,
        MandateSignerItem? existente)
    {
        if (signatureVaultId is not null || existente?.SignatureVaultId is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        return existente is not null
            && (!string.IsNullOrWhiteSpace(existente.Email)
                || IdentidadResueltaOEnCurso.Contains(existente.IdentityStatus, StringComparer.OrdinalIgnoreCase));
    }
}
