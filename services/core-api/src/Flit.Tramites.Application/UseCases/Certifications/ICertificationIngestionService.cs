using Flit.Tramites.Domain.Certifications;

namespace Flit.Tramites.Application.UseCases.Certifications;

/// <summary>
/// <b>Único punto de escritura</b> del almacén canónico de certificaciones (HU #11304, ADR-0041).
/// </summary>
/// <remarks>
/// Todo lo que quiera dejar un dato de SOAT, RTM o registro mercantil pasa por aquí: la consulta al
/// proveedor, el OCR del PDF, la validación de SOAT del OT y la corrección manual. Que sea uno solo es
/// lo que hace posible una regla de precedencia; hoy cada escritor sobrescribe a su manera y el último
/// en llegar gana, sin mirar quién había puesto el valor anterior.
///
/// <para>La ingesta es <b>best-effort respecto al llamador</b>: nunca debe tumbar una consulta ya
/// respondida ni una escritura de <c>field_values</c> ya persistida. Un fallo aquí degrada al camino
/// anterior, no rompe el trámite.</para>
/// </remarks>
public interface ICertificationIngestionService
{
    /// <summary>
    /// Persiste lo certificado por una fuente, fusionándolo dato a dato con lo que ya hubiera según
    /// <see cref="CertificationPrecedence"/>, y guarda la respuesta cruda sanitizada como evidencia.
    /// </summary>
    /// <returns>Cuántas filas quedaron escritas. Cero significa que no había nada que persistir.</returns>
    Task<int> IngestAsync(
        Guid instanceId,
        Guid tenantId,
        CertificationBundle bundle,
        CertificationProvenance provenance,
        RawProviderPayload? rawPayload = null,
        CancellationToken cancellationToken = default);
}
