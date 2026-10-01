using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Resultado de aplicar la plantilla de un formato a los datos del mandato.
/// </summary>
/// <param name="Data">Datos listos para el generador (con el cuerpo de la versión como plantilla editor, si hay).</param>
/// <param name="FormatCode">Formato usado; <c>null</c> si el OT tiene plantilla propia heredada (no aplica).</param>
/// <param name="FormatVersion">Versión usada: 0 = redacción del generador; N = versión N; <c>null</c> si no aplica.</param>
public sealed record MandatoFormatApplication(MandatoData Data, string? FormatCode, int? FormatVersion);

/// <summary>
/// <b>Único camino</b> por el que el contrato del trámite, el simulador y la vista previa del organismo toman la
/// plantilla publicada de un formato (HU #13172, Feature #13118).
/// <para>Reglas: (1) la plantilla propia del OT que la HU #11705 retiró de la interfaz sigue mandando donde exista
/// (<c>CustomTemplateKind</c> editor/pdf): no se reactiva ni se pisa; (2) sin personalización publicada se emite la
/// redacción del generador, sin cambios; (3) con ella, el cuerpo entra por el camino del editor del generador, que
/// sustituye las variables y deja en blanco las que no tienen dato; (4) con <c>pinnedVersion</c> se reproduce la versión
/// con la que se emitió el contrato, no la vigente.</para>
/// </summary>
public static class MandatoFormatTemplateApplier
{
    public static async Task<MandatoFormatApplication> ApplyAsync(
        MandatoData data,
        IMandateFormatTemplateProvider? provider,
        int? pinnedVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (provider is null || MandatoCustomTemplateKindCodes.HasCustom(data.CustomTemplateKind))
            return new MandatoFormatApplication(data, null, null);

        var template = await provider.ResolveAsync(data.TemplateCode, pinnedVersion, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(template.Body))
            return new MandatoFormatApplication(data, template.FormatCode, template.VersionNumber);

        return new MandatoFormatApplication(
            data with
            {
                CustomTemplateKind = MandatoCustomTemplateKindCodes.Editor,
                CustomTemplateBody = template.Body,
            },
            template.FormatCode,
            template.VersionNumber);
    }
}
