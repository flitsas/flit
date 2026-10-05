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
/// <para><b>Sin excepción de firma física (HU #13131, ADR-0061).</b> La firma física ya no es una forma
/// de firma: todo organismo exige baúl o biometría. Las filas históricas con <c>signs_physically</c> no se
/// borran ni cambian el resolver de trámites (F4), pero ya no eximen de esta comprobación.</para>
///
/// <para><b>La identidad en curso cuenta.</b> Basta con que la validación biométrica esté en camino
/// (<c>pending</c>) para no bloquear al mandatario mientras Kyverum resuelve.</para>
/// </summary>
public static class MandateSignerSigningCapability
{
    public const string Field = "transitOfficeIds";

    public const string SinMedioDeFirmaMessage =
        "El mandatario aún no puede firmar en los organismos elegidos: no tiene una firma guardada en el "
        + "baúl ni su validación de identidad aprobada o en curso. Guarda su firma en el baúl de firmas o "
        + "elige «Validación de identidad» como forma de firma.";

    public const string SinFirmaDelBaulMessage =
        "Elige la firma del baúl del mandatario. Para firmar con el baúl necesita una firma activa y vigente.";

    public const string FieldVault = "signatureVaultId";

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
    /// <param name="signatureVaultId">Firma del baúl elegida en la petición.</param>
    /// <param name="existente">
    /// Mandatario ya registrado, en la edición. <c>null</c> en el alta. Aporta la firma y la identidad
    /// que ya tiene, para no exigir que se vuelvan a mandar en cada guardado.
    /// </param>
    public static IReadOnlyList<Guid> OrganismosSinMedioDeFirma(
        IReadOnlyList<Guid> offices,
        Guid? signatureVaultId,
        MandateSignerItem? existente = null,
        string? signatureMethod = null)
    {
        ArgumentNullException.ThrowIfNull(offices);

        // HU #13129 — con forma de firma explícita no hay caída de un medio al otro (ADR-0061).
        // Biometría: la validación la origina y la vigila el módulo Identidad (HU #13130; sin ventana de 30 días, HU #13130b), no
        // se exige aprobada para guardar. Baúl: exige la firma elegida.
        if (signatureMethod == MandateSignatureMethods.Biometria)
        {
            return [];
        }

        if (signatureMethod == MandateSignatureMethods.Baul)
        {
            if (signatureVaultId is { } v && v != Guid.Empty || existente?.SignatureVaultId is not null)
            {
                return [];
            }
        }
        else if (PuedeFirmarElectronicamente(signatureVaultId, existente))
        {
            return [];
        }

        return [.. offices];
    }

    /// <summary>
    /// Error 422 listo para devolver, o <c>null</c> si el mandatario puede firmar en todos los
    /// organismos indicados.
    /// </summary>
    public static MandateSignerValidationError? Validate(
        IReadOnlyList<Guid> offices,
        Guid? signatureVaultId,
        MandateSignerItem? existente = null,
        string? signatureMethod = null)
    {
        var sinFirma = OrganismosSinMedioDeFirma(offices, signatureVaultId, existente, signatureMethod);

        if (sinFirma.Count == 0)
        {
            return null;
        }

        return signatureMethod == MandateSignatureMethods.Baul
            ? new MandateSignerValidationError(FieldVault, SinFirmaDelBaulMessage, null)
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
