namespace Flit.Tramites.Application.BulkTramites;

/// <summary>Archivo XLSX de plantilla ya generado, listo para devolver como descarga.</summary>
public sealed record RenderedBulkTramitesTemplate(string Filename, string Mimetype, byte[] Content);
