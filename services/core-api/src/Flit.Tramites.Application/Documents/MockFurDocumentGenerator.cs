using System.Globalization;
using System.Text;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Generador FUR/compraventa de TEXTO PLANO (.txt) — SOLO para tests, SIN registro en DI. La
/// implementación productiva es <c>FurOverlayDocumentGenerator</c> (overlay PdfSharpCore, HU #10256).
/// Se conserva porque varios tests de handler dependen de un <see cref="IFurDocumentGenerator"/>
/// liviano y determinista (no ejercita librería de PDF).
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
        var v = data.Vehiculo;
        sb.AppendLine($"Marca: {v.Marca ?? "-"}");
        sb.AppendLine($"Línea: {v.Linea ?? "-"}");
        sb.AppendLine($"Modelo (año): {v.Modelo ?? "-"}");
        sb.AppendLine($"Color: {v.Color ?? "-"}");
        sb.AppendLine($"Clase: {v.Clase ?? "-"}");
        sb.AppendLine($"Combustible: {v.Combustible ?? "-"}");
        sb.AppendLine($"Cilindraje: {v.Cilindraje ?? "-"}");
        sb.AppendLine($"VIN: {v.Vin ?? "-"}");
        sb.AppendLine($"Placa: {v.Placa ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("-- Organismo de tránsito --");
        var o = data.Organismo;
        sb.AppendLine($"Código: {o.Codigo ?? "-"}");
        sb.AppendLine($"Nombre: {o.Nombre ?? "-"}");
        sb.AppendLine($"Ciudad: {o.Ciudad ?? "-"}");
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
