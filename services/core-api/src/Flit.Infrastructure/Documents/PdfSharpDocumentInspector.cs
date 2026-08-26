using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Implementación de <see cref="IPdfDocumentInspector"/> con PdfSharpCore (misma librería que el resto
/// de la generación PDF de Infrastructure, p. ej. <c>PdfSharpPageExtractor</c>). Abre en modo
/// <c>InformationOnly</c> — no muta el documento — para contar páginas y detectar cifrado; cualquier
/// excepción de parseo se traduce a <c>IsParseable=false</c> (nunca se propaga al caller).
/// </summary>
internal sealed partial class PdfSharpDocumentInspector(ILogger<PdfSharpDocumentInspector> logger) : IPdfDocumentInspector
{
    public PdfInspectionResult Inspect(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly);
            // DocumentSecurityLevel refleja el /Encrypt del PDF leído (no solo el nivel a aplicar al
            // escribir): distinto de None ⇒ el documento está cifrado, aunque haya podido abrirse sin
            // pedir contraseña (p. ej. solo con owner password). No hay API para "descifrar y fusionar"
            // en PDFsharp, así que cualquier nivel de cifrado se rechaza (pdf_cifrado).
            var isEncrypted = document.SecuritySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None;
            return new PdfInspectionResult(IsParseable: true, IsEncrypted: isEncrypted, PageCount: document.PageCount);
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            LogInspectFailed(logger, ex.Message);
            return new PdfInspectionResult(IsParseable: false, IsEncrypted: false, PageCount: 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo inspeccionar el PDF personalizado: {Detail}")]
    private static partial void LogInspectFailed(ILogger logger, string detail);
}
