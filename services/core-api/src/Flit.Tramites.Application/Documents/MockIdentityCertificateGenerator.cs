using System.Globalization;
using System.Text;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// MOCK Certificado de validación de identidad — sin librería PDF; swap a generador real (deuda).
/// Produce el contenido del documento como TEXTO PLANO (.txt) con el comprador (nombre/doc) y el
/// resultado de la biométrica (score + APROBADO/RECHAZADO). El pipeline (ensamblado + persistencia
/// vía IAttachmentStorage) es real; solo el render del documento es placeholder.
///
/// TODO(slice-fur-real): reemplazar por un generador real (plantilla PDF) que emita el certificado
/// oficial. Implementar IIdentityCertificateGenerator y cambiar el registro en DI — los handlers no cambian.
/// </summary>
public sealed class MockIdentityCertificateGenerator : IIdentityCertificateGenerator
{
    public GeneratedDocument GenerateIdentityCertificate(IdentityCertificateData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        sb.AppendLine("=== CERTIFICADO DE VALIDACIÓN DE IDENTIDAD — DOCUMENTO MOCK ===");
        sb.AppendLine("// MOCK CERTIFICADO IDENTIDAD — sin librería PDF; swap a generador real (deuda)");
        sb.AppendLine();
        sb.AppendLine($"Referencia trámite: {data.ReferenceNumber}");
        sb.AppendLine($"Instancia: {data.ProcedureInstanceId:D}");
        sb.AppendLine();
        sb.AppendLine("-- Comprador --");
        sb.AppendLine($"Nombre: {data.CompradorNombre}");
        sb.AppendLine($"Documento: {data.CompradorDocumento}");
        sb.AppendLine();
        sb.AppendLine("-- Resultado validación biométrica --");
        sb.AppendLine($"Score: {data.Score.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Resultado: {data.Resultado}");
        sb.AppendLine();
        sb.AppendLine($"Generado (mock): {DateTimeOffset.UtcNow:O}");

        var safeRef = data.ReferenceNumber.Replace('/', '-');
        var filename = $"certificado_identidad_{safeRef}.txt";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new GeneratedDocument("certificado_identidad", filename, "text/plain", bytes);
    }
}
