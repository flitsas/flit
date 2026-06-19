namespace Flit.Tramites.Application.Documents;

/// <summary>Datos de una parte para el documento (FUR / compraventa).</summary>
public sealed record DocumentParte(string Rol, string? Nombre, string? Documento, string? Email);

/// <summary>
/// Datos del trámite ensamblados para generar los documentos. Vehículo (vin/placa), partes
/// (comprador/vendedor), valor, causal y referencias del sello de firma.
/// </summary>
public sealed record FurDocumentData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string Modalidad,
    string? TipologiaCodigo,
    string? Vin,
    string? Placa,
    IReadOnlyList<DocumentParte> Partes,
    decimal? ValorVenta,
    string? Causal,
    IReadOnlyList<string> SellosFirma);

/// <summary>Un documento generado, listo para persistir vía IAttachmentStorage.</summary>
public sealed record GeneratedDocument(string Tipo, string Filename, string Mimetype, byte[] Content);

/// <summary>
/// Contrato del generador de documentos del trámite (FUR + contrato de compraventa). La
/// implementación actual es un MOCK (<see cref="MockFurDocumentGenerator"/>) que produce un
/// placeholder de texto con los datos reales — SIN librería de PDF. Se reemplaza por un generador
/// real (plantilla PDF) sin tocar los handlers — mismo patrón contract-first que el scorer/proveedor.
/// </summary>
public interface IFurDocumentGenerator
{
    /// <summary>Genera el FUR (Formulario Único de Registro) con los datos del trámite.</summary>
    GeneratedDocument GenerateFur(FurDocumentData data);

    /// <summary>Genera el contrato de compraventa (solo traspaso) con los datos del trámite.</summary>
    GeneratedDocument GenerateCompraventa(FurDocumentData data);
}
