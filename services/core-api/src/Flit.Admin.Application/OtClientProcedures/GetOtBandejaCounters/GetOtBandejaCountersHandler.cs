using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;

/// <summary>Petición de los contadores de la cabecera de la bandeja del OT.</summary>
public sealed class GetOtBandejaCountersQuery
{
    public Guid OtTenantId { get; init; }
    /// <summary>Override de organismo para SuperAdmin; null = el del perfil OT del tenant.</summary>
    public Guid? TransitOfficeId { get; init; }
}

/// <summary>
/// Resultado de los contadores. <see cref="TransitOfficeResolved"/> distingue "el tenant no tiene
/// organismo" de "el organismo no tiene trabajo": sin esa distinción el cliente pintaría seis ceros
/// en un caso que en realidad es un problema de configuración.
/// </summary>
public sealed class GetOtBandejaCountersResult
{
    public bool TransitOfficeResolved { get; init; }
    public int SinAsignarPlaca { get; init; }
    public int ConPlacaAsignada { get; init; }
    public int Aprobados { get; init; }
    public int Rechazados { get; init; }
    public int SinGestion { get; init; }
}

/// <summary>
/// Contadores de la cabecera de la bandeja del OT: cuánto trabajo hay de cada clase. Se calculan en
/// el repositorio, en SQL y sobre el universo accesible, porque la bandeja va paginada y contar la
/// página respondería "cuántos de estos veinte" en lugar de "cuántos hay".
/// </summary>
public sealed class GetOtBandejaCountersHandler
{
    private readonly IOtClientProcedureRepository _repository;

    public GetOtBandejaCountersHandler(IOtClientProcedureRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GetOtBandejaCountersResult> HandleAsync(
        GetOtBandejaCountersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var counters = await _repository.GetBandejaCountersAsync(
            query.OtTenantId,
            query.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        if (counters is null)
        {
            return new GetOtBandejaCountersResult { TransitOfficeResolved = false };
        }

        return new GetOtBandejaCountersResult
        {
            TransitOfficeResolved = true,
            SinAsignarPlaca = counters.SinAsignarPlaca,
            ConPlacaAsignada = counters.ConPlacaAsignada,
            Aprobados = counters.Aprobados,
            Rechazados = counters.Rechazados,
            SinGestion = counters.SinGestion,
        };
    }
}
