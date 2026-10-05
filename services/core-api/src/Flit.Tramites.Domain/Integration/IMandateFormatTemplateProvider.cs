namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Plantilla de un formato de contrato de mandato resuelta para generar un documento (HU #13172, Feature #13118).
/// </summary>
/// <param name="FormatCode">Código del formato (<c>template_code</c>).</param>
/// <param name="VersionNumber">
/// Versión usada: <c>0</c> = sin personalización (rige la redacción del generador); <c>N</c> = versión N publicada.
/// </param>
/// <param name="Body">Cuerpo con variables <c>{{...}}</c> de la versión, o <c>null</c> si es la redacción del generador.</param>
public sealed record MandateFormatTemplate(string FormatCode, int VersionNumber, string? Body);

/// <summary>
/// Puerto hacia las plantillas versionadas que el Super Admin publica por formato (HU #13169/#13171). Lo consumen la
/// generación del contrato del trámite y el simulador, para que ambos sigan el mismo camino.
/// </summary>
public interface IMandateFormatTemplateProvider
{
    /// <summary>
    /// Resuelve la plantilla del formato. Con <paramref name="pinnedVersion"/> (la versión registrada en el contrato ya
    /// emitido) devuelve ESA versión aunque haya una más reciente; sin ella, la vigente (la última publicada).
    /// </summary>
    Task<MandateFormatTemplate> ResolveAsync(
        string formatCode,
        int? pinnedVersion,
        CancellationToken ct = default);
}
