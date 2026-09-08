using System.Globalization;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Texto de trazabilidad de la firma del baúl (ADR-0025 §4 / HU #11170).
/// Fuente única para FUR, impronta manual y demás documentos.
/// </summary>
public static class FirmaBaulSelloText
{
    /// <summary>
    /// Arma el sello multilínea de una firma del baúl.
    /// </summary>
    /// <param name="incluirIdentificacion">
    /// <c>true</c> añade documento y nombre (como el FUR); <c>false</c> solo vigencia y hash.
    /// </param>
    public static string Build(FirmaBaulMetadata meta, bool incluirIdentificacion)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var lines = new List<string>();
        if (incluirIdentificacion)
        {
            lines.Add($"Doc. {meta.DocumentNumber}");
            lines.Add(meta.FullName);
        }

        lines.Add(
            $"Vig. {meta.VigenciaDesde.ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture)} — {meta.VigenciaHasta.ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(meta.Hash))
            lines.Add($"Hash: {meta.Hash}");

        return string.Join('\n', lines);
    }
}
