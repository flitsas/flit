using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Solicitud de listado FILTRADO y ORDENADO server-side — a diferencia de
/// <see cref="ListProcedureInstancesHandler"/> (trae el TOP-N más reciente sin filtros ni paginación
/// real). <see cref="SortBy"/> viaja SIN VALIDAR: la whitelist la aplica
/// <see cref="ProcedureInstanceSortFields.Resolve"/> dentro del handler.
/// </summary>
public sealed record ProcedureInstanceListRequest
{
    public Guid? TenantId { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = ListProcedureInstancesHandler.MaxItems;

    public string? Vin { get; init; }
    public string? Placa { get; init; }
    public string? Vendedor { get; init; }
    public string? Comprador { get; init; }
    public string? Gestor { get; init; }
    public bool? Firmado { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedTo { get; init; }

    /// <summary>Estados a incluir (OR). Vacío = todos.</summary>
    public IReadOnlyList<string>? Estados { get; init; }
    /// <summary>Familia del trámite (MATRICULAS / TRASPASO / OTROS).</summary>
    public string? Modalidad { get; init; }
    /// <summary>Nombre del organismo de tránsito, por subcadena.</summary>
    public string? OrganismoTransito { get; init; }
    /// <summary>Código del tipo concreto de trámite, no la familia.</summary>
    public string? TipoCodigo { get; init; }

    public string? SortBy { get; init; }
    public bool SortDescending { get; init; } = true;
}

/// <summary>
/// Lista blanca de valores aceptados para el parámetro público <c>sortBy</c> del endpoint. RNF de la
/// HU: un valor no reconocido (typo, campo inexistente, intento de inyección) SIEMPRE cae al orden por
/// defecto — nunca lanza, nunca se concatena en SQL. Acepta alias camelCase y snake_case porque ambos
/// circulan en distintos consumidores (frontend TS en camelCase; algunos scripts/QA en snake_case).
/// </summary>
public static class ProcedureInstanceSortFields
{
    private static readonly Dictionary<string, ProcedureInstanceSortBy> Whitelist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["comprador"] = ProcedureInstanceSortBy.Comprador,
            ["createdAt"] = ProcedureInstanceSortBy.CreatedAt,
            ["created_at"] = ProcedureInstanceSortBy.CreatedAt,
            ["updatedAt"] = ProcedureInstanceSortBy.UpdatedAt,
            ["updated_at"] = ProcedureInstanceSortBy.UpdatedAt,
            ["gestor"] = ProcedureInstanceSortBy.Gestor,
            ["placa"] = ProcedureInstanceSortBy.Placa,
            ["plate"] = ProcedureInstanceSortBy.Placa,
            ["vin"] = ProcedureInstanceSortBy.Vin,
        };

    /// <summary>
    /// Resuelve <paramref name="sortBy"/> contra la lista blanca; <see cref="ProcedureInstanceSortBy.Default"/>
    /// para null/vacío o cualquier valor no reconocido (incluidos intentos de inyección: nunca se usa el
    /// string crudo para construir SQL, así que lo peor que puede pasar es caer al orden por defecto).
    /// </summary>
    public static ProcedureInstanceSortBy Resolve(string? sortBy) =>
        !string.IsNullOrWhiteSpace(sortBy) && Whitelist.TryGetValue(sortBy.Trim(), out var field)
            ? field
            : ProcedureInstanceSortBy.Default;
}

/// <summary>
/// Orquesta el listado filtrado/ordenado: delega el <c>WHERE</c>/<c>ORDER BY</c> al repositorio (SQL) y
/// reutiliza el mismo mapeo a <see cref="InstanceSummaryDto"/> de <see cref="ListProcedureInstancesHandler"/>
/// (nombres de compañía/gestor en lote, identidad vigente por persona, vigencia del baúl) para que las
/// dos rutas del listado (histórica y filtrada) muestren exactamente las mismas columnas derivadas.
/// </summary>
public sealed class ListProcedureInstancesFilteredHandler(IProcedureInstanceRepository repo)
{
    private static readonly IReadOnlyDictionary<Guid, string> EmptyNames = new Dictionary<Guid, string>();
    private static readonly IReadOnlyDictionary<string, bool> EmptyFirmaBaul = new Dictionary<string, bool>();

    /// <summary>Hora de Colombia (UTC-5, sin DST) — igual que <see cref="ListProcedureInstancesHandler"/>:
    /// la vigencia del baúl se cuenta por día calendario local.</summary>
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    public async Task<(IReadOnlyList<InstanceSummaryDto> Items, int Total)> HandleAsync(
        ProcedureInstanceListRequest request, CancellationToken ct = default)
    {
        var sortBy = ProcedureInstanceSortFields.Resolve(request.SortBy);
        var direction = request.SortDescending ? SortDirection.Descending : SortDirection.Ascending;

        var filter = new ProcedureInstanceListFilter
        {
            Vin = request.Vin,
            Placa = request.Placa,
            Vendedor = request.Vendedor,
            Comprador = request.Comprador,
            Gestor = request.Gestor,
            Firmado = request.Firmado,
            CreatedFrom = request.CreatedFrom,
            CreatedTo = request.CreatedTo,
            UpdatedFrom = request.UpdatedFrom,
            UpdatedTo = request.UpdatedTo,
            Estados = request.Estados,
            Modalidad = request.Modalidad,
            OrganismoTransito = request.OrganismoTransito,
            TipoCodigo = request.TipoCodigo,
        };

        var take = request.Take <= 0 || request.Take > ListProcedureInstancesHandler.MaxItems
            ? ListProcedureInstancesHandler.MaxItems
            : request.Take;

        var (instances, total) = await repo.ListWithSummaryGraphFilteredAsync(
            request.TenantId, Math.Max(0, request.Skip), take, filter, sortBy, direction, ct);

        // Mismo enriquecimiento en lote (sin N+1) que ListProcedureInstancesHandler.HandleAsync.
        IReadOnlyDictionary<Guid, string> nombres =
            await repo.GetTenantNamesAsync(instances.Select(i => i.TenantId).ToList(), ct) ?? EmptyNames;

        IReadOnlyDictionary<Guid, string> gestores =
            await repo.GetUserDisplayNamesAsync(instances.Select(i => i.CreatedByUserId).ToList(), ct)
            ?? EmptyNames;

        var now = DateTimeOffset.UtcNow;
        IReadOnlySet<string> identidadKeys = await repo.ListVigenteApprovedIdentityKeysAsync(
            instances.Select(i => i.TenantId).Distinct().ToList(), now, ct) ?? new HashSet<string>();

        var hoy = DateOnly.FromDateTime(now.ToOffset(ColombiaUtcOffset).DateTime);
        IReadOnlyDictionary<string, bool> firmaBaul = await repo.ListFirmaBaulVigenciaKeysAsync(
            instances.Select(i => i.TenantId).Distinct().ToList(), hoy, ct) ?? EmptyFirmaBaul;

        var items = instances
            .Select(e => ListProcedureInstancesHandler.ToSummary(
                e,
                IdentityApprovalResolver.ApprovedPartiesFromKeys(e, identidadKeys, now, firmaBaul),
                nombres.GetValueOrDefault(e.TenantId),
                gestores.GetValueOrDefault(e.CreatedByUserId),
                firmaBaul))
            .ToList();

        return (items, total);
    }
}

/// <summary>
/// Conteo por estado para la tira de KPIs del listado. Devuelve SIEMPRE las siete claves del
/// vocabulario —con cero donde no hay filas— para que la tira pinte las siete tarjetas sin que el
/// cliente tenga que rellenar huecos.
/// </summary>
public sealed class CountProcedureInstancesByStatusHandler(IProcedureInstanceRepository repo)
{
    public async Task<IReadOnlyDictionary<string, int>> HandleAsync(
        ProcedureInstanceListRequest request, CancellationToken ct = default)
    {
        // `Estados` se ignora a propósito: las tarjetas dicen cuántos hay de CADA estado bajo el resto
        // de criterios. Acotarlas al estado ya seleccionado dejaría las otras seis en cero y el gestor
        // no podría ver a dónde moverse.
        var filter = new ProcedureInstanceListFilter
        {
            Vin = request.Vin,
            Placa = request.Placa,
            Vendedor = request.Vendedor,
            Comprador = request.Comprador,
            Gestor = request.Gestor,
            Firmado = request.Firmado,
            CreatedFrom = request.CreatedFrom,
            CreatedTo = request.CreatedTo,
            UpdatedFrom = request.UpdatedFrom,
            UpdatedTo = request.UpdatedTo,
            Modalidad = request.Modalidad,
            OrganismoTransito = request.OrganismoTransito,
            TipoCodigo = request.TipoCodigo,
        };

        var conteos = await repo.CountByStatusFilteredAsync(request.TenantId, filter, ct);

        // `Todos` MÁS `Subsanacion`: ese último es legado —la subsanación viva es un flag sobre
        // `rechazado`— pero sigue habiendo filas migradas con ese status, y la tira del listado pinta
        // su tarjeta. Omitirlo aquí la dejaría sin número en vez de en cero.
        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var estado in TramiteEstado.Todos)
            resultado[estado] = conteos.GetValueOrDefault(estado);
        resultado[TramiteEstado.Subsanacion] = conteos.GetValueOrDefault(TramiteEstado.Subsanacion);

        return resultado;
    }
}
