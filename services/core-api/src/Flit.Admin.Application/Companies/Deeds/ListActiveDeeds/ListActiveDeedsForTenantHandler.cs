using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;

/// <summary>
/// Caso de uso del consumo del wizard (HU #10903, ADR-0033 §5.4): las escrituras ACTIVAS y VIGENTES
/// del tenant, proyectadas por compañía representada (NIT + razón social) con los días restantes de
/// vigencia. Reutiliza <see cref="IDeedReader.ListActiveVigentesAsync"/> (filtra por <c>is_active</c>
/// + rango de vigencia contra "hoy" en Colombia) y resuelve el NIT/nombre de cada compañía cubierta
/// vía <see cref="ILegalRepresentativeReader.ListRepresentedCompaniesAsync"/>.
///
/// Una escritura puede cubrir varias compañías (puente M:N) y una compañía puede aparecer en varias
/// escrituras vigentes: la salida se colapsa a UNA fila por NIT, conservando la escritura de mayor
/// vigencia (la más días restantes), y se ordena por vencimiento más próximo primero para que el FE
/// resalte con badges las que están por vencer. El "hoy" se ancla a la hora de Colombia (UTC-5) vía
/// <see cref="TimeProvider"/>, coherente con el resto del cálculo de vigencia.
/// </summary>
public sealed class ListActiveDeedsForTenantHandler
{
    // Hora de Colombia (UTC-5, sin DST): la vigencia se cuenta por día calendario local (ADR-0025 §3).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly IDeedReader _deedReader;
    private readonly ILegalRepresentativeReader _representativeReader;
    private readonly TimeProvider _timeProvider;

    public ListActiveDeedsForTenantHandler(
        IDeedReader deedReader,
        ILegalRepresentativeReader representativeReader,
        TimeProvider timeProvider)
    {
        _deedReader = deedReader ?? throw new ArgumentNullException(nameof(deedReader));
        _representativeReader = representativeReader ?? throw new ArgumentNullException(nameof(representativeReader));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<ActiveDeedResponse>> HandleAsync(
        ListActiveDeedsForTenantQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);

        var deeds = await _deedReader
            .ListActiveVigentesAsync(query.TenantId, today, cancellationToken)
            .ConfigureAwait(false);

        if (deeds.Count == 0)
        {
            return [];
        }

        var companies = await _representativeReader
            .ListRepresentedCompaniesAsync(query.TenantId, cancellationToken)
            .ConfigureAwait(false);
        var companiesById = companies.ToDictionary(c => c.Id);

        // Colapso por NIT: conserva la escritura de mayor vigencia por compañía (la más días restantes).
        var bestByNit = new Dictionary<string, ActiveDeedResponse>(StringComparer.Ordinal);
        foreach (var deed in deeds)
        {
            var diasRestantes = deed.VigenciaHasta.DayNumber - today.DayNumber;
            foreach (var companyId in deed.RepresentedCompanyIds)
            {
                if (!companiesById.TryGetValue(companyId, out var company))
                {
                    continue;
                }

                var candidate = new ActiveDeedResponse(
                    company.DocumentNumber,
                    company.Name,
                    diasRestantes,
                    deed.VigenciaHasta);

                if (!bestByNit.TryGetValue(company.DocumentNumber, out var current)
                    || candidate.VigenciaHasta > current.VigenciaHasta)
                {
                    bestByNit[company.DocumentNumber] = candidate;
                }
            }
        }

        return [.. bestByNit.Values
            .OrderBy(d => d.VigenciaHasta)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
