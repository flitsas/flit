using System.Globalization;
using System.Text;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// MOCK FUR — sin librería PDF; swap a generador real (deuda).
/// Produce el contenido del documento como TEXTO PLANO (.txt) con los datos reales del trámite
/// (vehículo, partes, valor, causal, sellos de firma). El pipeline (ensamblado de datos + gating +
/// persistencia vía IAttachmentStorage) es real; solo el render del documento es placeholder.
///
/// TODO(slice-fur-real): reemplazar por un generador real (plantilla PDF) que emita el FUR oficial
/// y el contrato de compraventa. Implementar IFurDocumentGenerator y cambiar el registro en DI —
/// los handlers no cambian.
/// </summary>
public sealed class MockFurDocumentGenerator : IFurDocumentGenerator
{
    public GeneratedDocument GenerateFur(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        sb.AppendLine("=== FUR (Formulario Único de Registro) — DOCUMENTO MOCK ===");
        sb.AppendLine("// MOCK FUR — sin librería PDF; swap a generador real (deuda)");
        AppendComun(sb, data);
        return Build("fur", "fur", data.ReferenceNumber, sb);
    }

    public GeneratedDocument GenerateCompraventa(FurDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        sb.AppendLine("=== CONTRATO DE COMPRAVENTA — DOCUMENTO MOCK ===");
        sb.AppendLine("// MOCK FUR — sin librería PDF; swap a generador real (deuda)");
        AppendComun(sb, data);
        return Build("compraventa", "compraventa", data.ReferenceNumber, sb);
    }

    private static void AppendComun(StringBuilder sb, FurDocumentData data)
    {
        sb.AppendLine();
        sb.AppendLine($"Referencia trámite: {data.ReferenceNumber}");
        sb.AppendLine($"Instancia: {data.ProcedureInstanceId:D}");
        sb.AppendLine($"Modalidad: {data.Modalidad}");
        sb.AppendLine($"Tipología: {data.TipologiaCodigo ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("-- Vehículo --");
        sb.AppendLine($"VIN: {data.Vin ?? "-"}");
        sb.AppendLine($"Placa: {data.Placa ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("-- Partes --");
        foreach (var p in data.Partes)
            sb.AppendLine($"{p.Rol}: {p.Nombre ?? "-"} (doc {p.Documento ?? "-"}, {p.Email ?? "-"})");
        sb.AppendLine();
        sb.AppendLine("-- Comercial --");
        sb.AppendLine($"Valor venta: {(data.ValorVenta?.ToString("F2", CultureInfo.InvariantCulture)) ?? "-"}");
        sb.AppendLine($"Causal: {data.Causal ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("-- Sellos de firma electrónica --");
        if (data.SellosFirma.Count == 0)
            sb.AppendLine("(sin sellos)");
        else
            foreach (var s in data.SellosFirma)
                sb.AppendLine($"- {s}");
        sb.AppendLine();
        sb.AppendLine($"Generado (mock): {DateTimeOffset.UtcNow:O}");
    }

    private static GeneratedDocument Build(string tipo, string baseName, string reference, StringBuilder sb)
    {
        var safeRef = reference.Replace('/', '-');
        var filename = $"{baseName}_{safeRef}.txt";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new GeneratedDocument(tipo, filename, "text/plain", bytes);
    }
}
