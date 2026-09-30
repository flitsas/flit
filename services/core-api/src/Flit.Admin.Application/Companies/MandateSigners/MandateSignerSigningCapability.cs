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
/// <para><b>El correo NO cuenta (HU #13122, Epic #13090).</b> Tener correo no habilita a firmar: sin firma
/// activa del baúl ni validación biométrica válida o en curso, el mandatario no puede firmar. El
/// correo es solo un dato de contacto.</para>
///
/// <para><b>Excepción transitoria: la firma física.</b> Un organismo marcado en
/// <c>PhysicalSignatureOfficeIds</c> no exige baúl ni identidad al parametrizar (se puede dejar
/// línea en blanco). Si el mandatario ya tiene imagen o sello, el contrato las estampa igual:
/// el modelo a mano no las oculta.</para>
///
/// <para><b>La identidad en curso cuenta.</b> Basta con que la validación biométrica esté en camino
/// (<c>pending</c>) para no bloquear al mandatario mientras Kyverum resuelve. La firma física la retira
/// la Feature F2; hasta entonces se conserva la excepción.</para>
/// </summary>
public static class MandateSignerSigningCapability
{
    public const string Field = "transitOfficeIds";

    public const string SinMedioDeFirmaMessage =
        "El mandatario no está en condiciones de firmar en los organismos indicados: no tiene firma en "
        + "el baúl ni una validación biométrica válida o en curso. Carga su firma en el baúl de firmas "
        + "o inicia la validación biométrica en el módulo Identidad.";

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
    /// <param name="existente">
    /// Mandatario ya registrado, en la edición. <c>null</c> en el alta. Aporta la firma y la identidad
    /// que ya tiene, para no exigir que se vuelvan a mandar en cada guardado.
    /// </param>
    public static IReadOnlyList<Guid> OrganismosSinMedioDeFirma(
        IReadOnlyList<Guid> offices,
        IReadOnlyList<Guid>? physicalSignatureOfficeIds,
        Guid? signatureVaultId,
        MandateSignerItem? existente = null)
    {
        ArgumentNullException.ThrowIfNull(offices);

        if (PuedeFirmarElectronicamente(signatureVaultId, existente))
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
        MandateSignerItem? existente = null)
    {
        var sinFirma = OrganismosSinMedioDeFirma(
            offices, physicalSignatureOfficeIds, signatureVaultId, existente);

        return sinFirma.Count == 0
            ? null
            : new MandateSignerValidationError(Field, SinMedioDeFirmaMessage, null);
    }

    private static bool PuedeFirmarElectronicamente(
        Guid? signatureVaultId,
        MandateSignerItem? existente)
    {
        if (signatureVaultId is not null || existente?.SignatureVaultId is not null)
        {
            return true;
        }

        return existente is not null
            && IdentidadResueltaOEnCurso.Contains(existente.IdentityStatus, StringComparer.OrdinalIgnoreCase);
    }
}
