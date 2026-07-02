using System.Globalization;
using System.Text;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Certificado de identidad de TEXTO PLANO (.txt) — SOLO para tests, SIN registro en DI. La
/// implementación productiva es <c>IdentityCertificatePdfGenerator</c> (QuestPDF, HU #10458). Se
/// conserva para tests que dependen de un <see cref="IIdentityCertificateGenerator"/> liviano y
/// determinista.
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
