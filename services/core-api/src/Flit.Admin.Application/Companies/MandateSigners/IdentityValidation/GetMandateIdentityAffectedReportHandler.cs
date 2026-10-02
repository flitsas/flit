using System.Text;
using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.IdentityValidation;

/// <summary>
/// HU #13247 (Feature #13245) — reporte de mandatarios afectados por la validación exclusiva: Persona natural con forma de
/// firma biometría sin validación propia aprobada. Trae compañía, organismo y correo para avisarles antes de desplegar en
/// PDN (la pérdida de firma es masiva y silenciosa si no se avisa). Solo Super Admin; el correo es PII (Ley 1581).
/// </summary>
public sealed class GetMandateIdentityAffectedReportHandler
{
    private readonly IMandateIdentityAffectedReader _reader;

    public GetMandateIdentityAffectedReportHandler(IMandateIdentityAffectedReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<MandateIdentityAffectedRow>> HandleAsync(
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

/// <summary>Exportación CSV del reporte (UTF-8 con BOM para Excel). Reutiliza el escape anti-inyección del reporte de la firma física.</summary>
public static class MandateIdentityAffectedCsv
{
    public const string FileName = "mandatarios-sin-validacion-propia.csv";

    private static readonly string[] Headers =
    [
        "mandatario_id", "mandatario", "correo", "compania_id", "compania", "organismo_id", "organismo_codigo",
        "organismo", "estado_identidad",
    ];

    public static string Build(IReadOnlyList<MandateIdentityAffectedRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append(string.Join(',', Headers)).Append("\r\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(',',
                    PhysicalSignatureMigrationCsv.Cell(r.MandateSignerId.ToString()),
                    PhysicalSignatureMigrationCsv.Cell(r.FullName),
                    PhysicalSignatureMigrationCsv.Cell(r.Email),
                    PhysicalSignatureMigrationCsv.Cell(r.CompanyTenantId?.ToString()),
                    PhysicalSignatureMigrationCsv.Cell(r.CompanyName),
                    PhysicalSignatureMigrationCsv.Cell(r.TransitOfficeId.ToString()),
                    PhysicalSignatureMigrationCsv.Cell(r.TransitOfficeCode),
                    PhysicalSignatureMigrationCsv.Cell(r.TransitOfficeName),
                    PhysicalSignatureMigrationCsv.Cell(r.IdentityStatus)))
                .Append("\r\n");
        }

        return sb.ToString();
    }
}
