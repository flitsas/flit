using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;

/// <summary>
/// HU #13195 (ADR-0066 D1) — reporte previo de solo lectura del colapso «un solo mandatario activo por
/// compañía, organismo y grupo de origen»: por compañía y organismo, qué vínculo se conserva y cuáles se
/// inactivarán, sin datos personales. No modifica ningún dato.
/// </summary>
public sealed class GetMandateLinkCollapseReportHandler
{
    private readonly IMandateSignerLinkCollapseReader _reader;

    public GetMandateLinkCollapseReportHandler(IMandateSignerLinkCollapseReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<MandateLinkCollapseRow>> HandleAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _reader.ListAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .OrderBy(r => r.TransitOfficeCode, StringComparer.Ordinal)
                .ThenBy(r => r.CompanyTenantId)
                .ThenBy(r => r.OriginGroup, StringComparer.Ordinal)
                .ThenBy(r => r.Action == MandateLinkCollapseActions.Conservar ? 0 : 1)
                .ThenBy(r => r.LinkId),
        ];
    }
}
