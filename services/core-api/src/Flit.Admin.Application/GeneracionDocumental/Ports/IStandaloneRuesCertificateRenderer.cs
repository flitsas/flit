namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>PDF ya renderizado, listo para ir a storage.</summary>
public sealed record RenderedStandaloneDocument(string Filename, string Mimetype, byte[] Content);

/// <summary>
/// Puerto acotado del renderizador del Certificado RUES standalone. El adaptador de Infrastructure
/// delega en el <c>IRuesCertificateGenerator</c> del expediente <b>sin modificarlo</b>: le pasa
/// <c>Guid.Empty</c> como identificador de trámite (convenio «sin instancia» que ya usa el preview
/// de RUNT) y una referencia sintética que el generador solo usa para nombrar el archivo. Así el
/// certificado standalone no puede divergir del del expediente: es el mismo generador.
/// </summary>
public interface IStandaloneRuesCertificateRenderer
{
    /// <summary>
    /// Renderiza el certificado a partir de los campos mercantiles congelados en el snapshot.
    /// </summary>
    /// <param name="ruesFields">Campos <c>rues_*</c> tal como los devolvió la consulta.</param>
    /// <param name="referenceNumber">Referencia sintética del documento (p. ej. <c>GD-1a2b3c4d</c>).</param>
    RenderedStandaloneDocument Render(
        IReadOnlyDictionary<string, string?> ruesFields,
        string referenceNumber);
}
