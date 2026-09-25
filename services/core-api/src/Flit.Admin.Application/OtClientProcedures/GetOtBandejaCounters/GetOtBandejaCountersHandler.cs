using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures.GetOtBandejaCounters;

/// <summary>Petición de los contadores de la cabecera de la bandeja del OT.</summary>
public sealed class GetOtBandejaCountersQuery
{
    public Guid OtTenantId { get; init; }
    /// <summary>Override de organismo para SuperAdmin; null = el del perfil OT del tenant.</summary>
    public Guid? TransitOfficeId { get; init; }

    /// <summary>
    /// Epic #12686 (HU #12803) — filtros de la bandeja (familia, búsqueda, condiciones…). Su estado y
    /// su marca de revocatoria se ignoran. <c>null</c> = todo lo recibido por el organismo.
    /// </summary>
    public OtClientProcedureFilter? Filtro { get; init; }
}

/// <summary>
/// Resultado de los contadores. <see cref="TransitOfficeResolved"/> distingue "el tenant no tiene
/// organismo" de "el organismo no tiene trabajo": sin esa distinción el cliente pintaría seis ceros
/// en un caso que en realidad es un problema de configuración.
/// </summary>
public sealed class GetOtBandejaCountersResult
{
    public bool TransitOfficeResolved { get; init; }
    /// <summary>ADR-0059 — radicados sin placa: cola de "asignar placa".</summary>
    public int Preasignacion { get; init; }
    /// <summary>ADR-0059 — con placa asignada; el gestor gestiona SOAT/impuestos y envía al OT.</summary>
    public int Asignados { get; init; }
    /// <summary>Entregados a la espera de la decisión del organismo.</summary>
    public int PorDecidir { get; init; }
    public int Aprobados { get; init; }
    public int Rechazados { get; init; }
    /// <summary>HU #12166/#12168 (Feature #12156) — Aprobados que el organismo revocó.</summary>
    public int Revocados { get; init; }

    /// <summary>Pedido del usuario (2026-09-16) — Aprobados con solicitud de revocatoria ACTIVA.</summary>
    public int SolicitudesRevocatoria { get; init; }
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
            query.Filtro,
            query.TransitOfficeId,
            cancellationToken).ConfigureAwait(false);

        if (counters is null)
        {
            return new GetOtBandejaCountersResult { TransitOfficeResolved = false };
        }

        return new GetOtBandejaCountersResult
        {
            TransitOfficeResolved = true,
            Preasignacion = counters.Preasignacion,
            Asignados = counters.Asignados,
            PorDecidir = counters.PorDecidir,
            Aprobados = counters.Aprobados,
            Rechazados = counters.Rechazados,
            Revocados = counters.Revocados,
            SolicitudesRevocatoria = counters.SolicitudesRevocatoria,
        };
    }
}
