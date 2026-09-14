namespace Flit.Tramites.Application.BulkTramites;

/// <summary>
/// Puerto para generar la plantilla XLSX de carga masiva de trámites (AC1/AC3 de HU #12520). La
/// plantilla de <see cref="BulkTramitesTemplateType.Otros"/> necesita el catálogo canónico de
/// tipos de trámite vigente, por eso el puerto es async — a diferencia del generador de
/// Generación Documental, que no depende de datos en BD.
/// </summary>
public interface IBulkTramitesXlsxTemplate
{
    /// <param name="tenantId">
    /// Empresa que descarga la plantilla. Hace falta para el desplegable de organismos de tránsito
    /// de Matrícula: solo se ofrecen los que esa empresa tiene habilitados.
    /// </param>
    Task<RenderedBulkTramitesTemplate> BuildAsync(
        BulkTramitesTemplateType tipo, Guid tenantId, CancellationToken ct = default);
}
