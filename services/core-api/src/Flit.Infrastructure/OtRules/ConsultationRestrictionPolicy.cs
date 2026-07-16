using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IConsultationRestrictionPolicy"/> (HU #10760): resuelve el OT
/// destino del trámite y lee de <c>admin.tenant_transit_office_consultation_restrictions</c> los
/// kinds que la compañía inhabilitó para ese OT.
///
/// Aquí ocurre la traducción entre los dos vocabularios espejo —
/// <see cref="RestrictedConsultationKinds"/> (Admin.Domain) y
/// <see cref="ConsultationRestrictionKinds"/> (Tramites.Application)— porque Infraestructura es la
/// única capa que ve ambos lados. La traducción es explícita (y no un passthrough de strings) para
/// que un kind nuevo en Admin que Trámites aún no sepa interpretar se DESCARTE en vez de colarse
/// como restricción silenciosa en el fan-out del preflight.
/// </summary>
internal sealed class ConsultationRestrictionPolicy : IConsultationRestrictionPolicy
{
    private readonly IOtConsultationRestrictionRepository _restrictions;
    private readonly ITransitGrantRepository _grants;

    public ConsultationRestrictionPolicy(
        IOtConsultationRestrictionRepository restrictions,
        ITransitGrantRepository grants)
    {
        _restrictions = restrictions ?? throw new ArgumentNullException(nameof(restrictions));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<ConsultationRestrictions> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var officeId = await TransitOfficeDestinationResolver
            .ResolveAsync(_grants, tenantId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (officeId is not { } office)
        {
            // Sin OT resoluble no hay par (tenant, OT) al que aplicar política: default permisivo,
            // el mismo de la tabla dispersa (ausencia de fila = se consulta).
            return ConsultationRestrictions.None;
        }

        var rows = await _restrictions
            .ListForOfficeAsync(tenantId, office, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return ConsultationRestrictions.None;
        }

        // Traduce cada fila (kind Admin → kind Trámites) conservando su estado enabled. Un kind que
        // Trámites no sepa interpretar se descarta (no se cuela como ajuste silencioso).
        var settings = new List<KeyValuePair<string, bool>>(rows.Count);
        foreach (var row in rows)
        {
            var kind = Translate(row.ConsultationKind);
            if (kind is not null)
                settings.Add(new KeyValuePair<string, bool>(kind, row.Enabled));
        }

        return ConsultationRestrictions.FromSettings(settings);
    }

    /// <summary>
    /// Mapea un kind de Admin al vocabulario de Trámites. <c>null</c> = kind que el preflight no sabe
    /// omitir (p. ej. uno añadido al CHECK sin tocar el fan-out): se ignora.
    /// </summary>
    private static string? Translate(string adminKind) => adminKind switch
    {
        _ when string.Equals(adminKind, RestrictedConsultationKinds.Rnmc, StringComparison.OrdinalIgnoreCase)
            => ConsultationRestrictionKinds.Rnmc,
        _ when string.Equals(adminKind, RestrictedConsultationKinds.Fines, StringComparison.OrdinalIgnoreCase)
            => ConsultationRestrictionKinds.Fines,
        _ => null,
    };
}
