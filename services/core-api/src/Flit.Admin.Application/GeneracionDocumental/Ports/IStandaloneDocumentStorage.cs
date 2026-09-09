namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>Archivo ya persistido en el storage: path opaco + hash + tamaño.</summary>
public sealed record StoredStandaloneDocument(string StoragePath, string Sha256, long SizeBytes);

/// <summary>
/// Puerto ACOTADO de almacenamiento del módulo de generación documental (Feature #12201,
/// ADR-0056-generacion-documental-standalone). El adaptador de Infrastructure delega en
/// <c>IAttachmentStorage</c> pasando el <b>tenantId</b> como clave de agrupación — que es lo que ese
/// primer parámetro realmente significa, con cuatro precedentes productivos (baúl de firmas,
/// improntas de identidad, escrituras y plantillas de mandato). <b>No se modifica
/// <c>IAttachmentStorage</c>.</b>
/// <para>Existe además por una restricción de compilación (C6): <c>Flit.Admin.Application</c> no
/// referencia <c>Flit.Tramites.Application</c> y no puede nombrar sus tipos.</para>
/// </summary>
public interface IStandaloneDocumentStorage
{
    /// <summary>
    /// Persiste el binario del documento generado y devuelve su identificador de almacenamiento,
    /// el SHA-256 y el tamaño.
    /// </summary>
    /// <param name="tenantId">Clave de agrupación del artefacto en el storage (no es una FK).</param>
    Task<StoredStandaloneDocument> SaveAsync(
        Guid tenantId,
        string tipo,
        string filename,
        Stream content,
        CancellationToken cancellationToken = default);
}
