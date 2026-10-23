using System.Text;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;

/// <summary>
/// HU #13131 (ADR-0061) — reporte de migración de la firma física: los mandatarios activos que dependen
/// SOLO de ella, con compañía, organismo, forma de firma actual y dato faltante, para migrarlos a baúl o
/// biometría antes de que F4 active el bloqueo en producción. Sin documento ni correo (PII).
/// </summary>
public sealed class GetPhysicalSignatureMigrationReportHandler
{
    private readonly IPhysicalSignatureMigrationReader _reader;

    public GetPhysicalSignatureMigrationReportHandler(IPhysicalSignatureMigrationReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<PhysicalSignatureMigrationRow>> HandleAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reader.ListAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .OrderBy(r => r.TransitOfficeName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.CompanyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}

/// <summary>Exportación CSV del reporte (UTF-8 con BOM para Excel; sin documento ni correo).</summary>
public static class PhysicalSignatureMigrationCsv
{
    public const string FileName = "mandatarios-firma-fisica-migracion.csv";

    private static readonly string[] Headers =
    [
        "mandatario_id", "mandatario", "compania_id", "compania", "organismo_id", "organismo_codigo",
        "organismo", "forma_de_firma_actual", "forma_de_firma_declarada", "dato_faltante",
    ];

    public static string Build(IReadOnlyList<PhysicalSignatureMigrationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append(string.Join(',', Headers)).Append("\r\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(',',
                    Cell(r.MandateSignerId.ToString()),
                    Cell(r.FullName),
                    Cell(r.CompanyTenantId?.ToString()),
                    Cell(r.CompanyName),
                    Cell(r.TransitOfficeId.ToString()),
                    Cell(r.TransitOfficeCode),
                    Cell(r.TransitOfficeName),
                    Cell(r.CurrentSignatureForm),
                    Cell(r.DeclaredSignatureMethod),
                    Cell(r.MissingData)))
                .Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapa una celda: comillas dobles, y neutraliza fórmulas (= + - @) que Excel ejecutaría al abrir un
    /// nombre escrito por un usuario (inyección CSV).
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
