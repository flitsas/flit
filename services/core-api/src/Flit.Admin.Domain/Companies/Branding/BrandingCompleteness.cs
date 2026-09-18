namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Completitud estructural de un <see cref="BrandingDraft"/> para publicar (HU #12413 AC6):
/// presencia de nombre, los tres colores y el logotipo — no formato ni contraste (eso es
/// <see cref="BrandingValidation"/> / <see cref="WcagContrast"/>). Envuelve
/// <see cref="BrandingDraft.MissingFields"/> (ya implementada en HU #12412) para que
/// <c>PublishBrandingHandler</c> tenga un punto único y testeable de la regla de AC6.
/// </summary>
public static class BrandingCompleteness
{
    /// <summary>
    /// Campos faltantes, en el vocabulario estable del contrato: <c>platformName</c>,
    /// <c>colors.primary</c>, <c>colors.secondary</c>, <c>colors.onPrimary</c>, <c>logo</c>.
    /// Vacío = borrador completo (publicable en cuanto a presencia; formato/contraste aparte).
    /// </summary>
    public static IReadOnlyList<string> MissingFields(BrandingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.MissingFields();
    }

    /// <summary>`true` si no falta ningún campo obligatorio (AC6).</summary>
    public static bool IsComplete(BrandingDraft draft) => MissingFields(draft).Count == 0;
}
