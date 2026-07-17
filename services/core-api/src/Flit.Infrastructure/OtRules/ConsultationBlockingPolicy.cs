using System.Collections.Generic;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IConsultationBlockingPolicy"/> (FEATURE 05): resuelve el OT
/// destino del trámite y lee de <c>admin.tenant_transit_office_blocking_policies</c> los overrides de
/// bloqueo que la compañía fijó para ese OT.
///
/// Aquí ocurre la traducción entre los dos vocabularios espejo — <see cref="BlockingCriteria"/>
/// (Admin.Domain) y <see cref="ConsultationBlockingCriteria"/> (Tramites.Application) — porque
/// Infraestructura es la única capa que ve ambos lados. La traducción es explícita (no un passthrough
/// de strings) para que un criterio nuevo en Admin que Trámites aún no sepa aplicar se DESCARTE en vez
/// de colarse como override silencioso en la severidad del preflight.
/// </summary>
internal sealed class ConsultationBlockingPolicy : IConsultationBlockingPolicy
{
    private readonly IOtBlockingPolicyRepository _policies;
    private readonly ITransitGrantRepository _grants;

    public ConsultationBlockingPolicy(
        IOtBlockingPolicyRepository policies,
        ITransitGrantRepository grants)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<ConsultationBlockingRules> GetAsync(
        Guid tenantId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var officeId = await TransitOfficeDestinationResolver
            .ResolveAsync(_grants, tenantId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (officeId is not { } office)
        {
            // Sin OT resoluble no hay par (tenant, OT) al que aplicar política: cada criterio usa su
            // default (mismo comportamiento que la tabla dispersa sin filas).
            return ConsultationBlockingRules.None;
        }

        var rows = await _policies
            .ListForOfficeAsync(tenantId, office, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return ConsultationBlockingRules.None;
        }

        var overrides = new List<KeyValuePair<string, bool>>(rows.Count);
        foreach (var row in rows)
        {
            var criterion = Translate(row.Criterion);
            if (criterion is not null)
                overrides.Add(new KeyValuePair<string, bool>(criterion, row.Blocks));
        }

        return ConsultationBlockingRules.From(overrides);
    }

    /// <summary>
    /// Mapea un criterio de Admin al vocabulario de Trámites. <c>null</c> = criterio que el preflight
    /// no sabe aplicar (p. ej. uno añadido al CHECK sin tocar la severidad): se ignora.
    /// </summary>
    private static string? Translate(string adminCriterion) => adminCriterion switch
    {
        _ when Eq(adminCriterion, BlockingCriteria.Soat) => ConsultationBlockingCriteria.Soat,
        _ when Eq(adminCriterion, BlockingCriteria.Rtm) => ConsultationBlockingCriteria.Rtm,
        _ when Eq(adminCriterion, BlockingCriteria.EstadoVehiculo) => ConsultationBlockingCriteria.EstadoVehiculo,
        _ when Eq(adminCriterion, BlockingCriteria.Fines) => ConsultationBlockingCriteria.Fines,
        _ when Eq(adminCriterion, BlockingCriteria.Rnmc) => ConsultationBlockingCriteria.Rnmc,
        _ => null,
    };

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
