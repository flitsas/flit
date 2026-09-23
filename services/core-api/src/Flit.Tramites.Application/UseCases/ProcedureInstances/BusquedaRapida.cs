using System.Collections.Concurrent;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Epic #12686 (HU #12805) — atajos de la «Búsqueda rápida» del listado que no se pueden expresar con
/// los filtros existentes. Viajan como un parámetro propio y NO como campos del catálogo de consultas:
/// así no aparecen en «+ Filtro», que la épica pide dejar como está.
///
/// <para>Los atajos que ya tienen filtro (en subsanación, rechazado desde preasignación, faltantes por
/// aprobar) los arma el cliente con esos filtros; aquí solo viven los que necesitan algo nuevo.</para>
/// </summary>
public static class BusquedaRapida
{
    /// <summary>Entregados con más de 5 días calendario desde su última entrada a Entregado.</summary>
    public const string MasDe5Dias = "mas_de_5_dias";

    /// <summary>Entregados con más de 10 días calendario desde su última entrada a Entregado.</summary>
    public const string MasDe10Dias = "mas_de_10_dias";

    /// <summary>Borradores con alguna parte sin identidad validada ni firma de baúl vigente.</summary>
    public const string SinFirmas = "sin_firmas";

    /// <summary>Borradores a los que les falta al menos un documento obligatorio.</summary>
    public const string SinDocumento = "sin_documento";

    /// <summary>Borradores que no pueden salir al OT: sin firmas, sin documento o pausados.</summary>
    public const string Pausados = "pausados";

    /// <summary>
    /// Trámites que el usuario tiene a su cargo HOY: el responsable asignado o, si nunca se
    /// reasignó, quien lo creó (el «gestor efectivo»). Decisión del 2026-09-23 (opción B): si Ana
    /// crea un trámite y se lo pasan a Carlos, aparece en el «Mis trámites» de Carlos, no en el de Ana.
    /// </summary>
    public const string MisTramites = "mis_tramites";

    public static readonly IReadOnlyList<string> Todos =
        [MasDe5Dias, MasDe10Dias, SinFirmas, SinDocumento, Pausados, MisTramites];

    /// <summary>Mensaje de error si el atajo no existe; <c>null</c> si es válido o no viene.</summary>
    public static string? Validate(string? atajo) =>
        string.IsNullOrWhiteSpace(atajo) || Todos.Contains(atajo.Trim(), StringComparer.OrdinalIgnoreCase)
            ? null
            : $"Búsqueda rápida no válida: '{atajo}'. Valores permitidos: {string.Join(", ", Todos)}.";

    internal static string? Normalizar(string? atajo) =>
        string.IsNullOrWhiteSpace(atajo) ? null : atajo.Trim().ToLowerInvariant();

    /// <summary>
    /// Instante de corte de «más de <paramref name="dias"/> días»: entró a Entregado antes del inicio
    /// del día (hora de Colombia) de hoy menos esos días. Entrar el día 1 y consultar el día 7 son 6
    /// días: pasa el corte de 5 y no el de 10. Días calendario, sin excluir fines de semana ni festivos.
    /// </summary>
    public static DateTimeOffset CorteDeDias(int dias, DateTimeOffset ahora)
    {
        var hoy = DateOnly.FromDateTime(ahora.ToOffset(ColombiaTime.Offset).DateTime);
        var dia = hoy.AddDays(-dias);
        // En UTC: Npgsql rechaza un DateTimeOffset con desfase distinto de cero en `timestamptz`.
        return new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), ColombiaTime.Offset).ToUniversalTime();
    }
}

/// <summary>
/// El universo de borradores a evaluar en memoria supera el tope: responder con un subconjunto
/// sería un resultado truncado que parece completo. El endpoint lo traduce a 422.
/// </summary>
public sealed class BusquedaRapidaDemasiadoAmpliaException(int total, int tope)
    : InvalidOperationException(
        $"Hay {total} borradores que revisar y el máximo es {tope}. Acota la búsqueda por compañía o por rango de fechas.")
{
    public int Total { get; } = total;
    public int Tope { get; } = tope;
}

/// <summary>
/// Traduce un atajo de la búsqueda rápida a filtros del repositorio.
///
/// <para><b>Días en gestión</b> se resuelve en SQL (historial de estados). <b>Sin firmas</b>, <b>sin
/// documento</b> y <b>pausados</b> dependen de reglas que viven en código —la acreditación por actor
/// (identidad, baúl, representante legal) y la matriz documental con sus excepciones—, así que se
/// evalúan en memoria sobre los borradores que cumplen el resto de filtros y se devuelven como una
/// lista de ids. Se reutilizan exactamente las reglas de la etiqueta del actor del listado
/// (<see cref="ListProcedureInstancesFilteredHandler.ToSummariesAsync"/>) y del gate de radicación
/// (<see cref="ChecklistMatrixCompleteness"/>): el atajo nunca dice algo distinto de lo que se ve.</para>
/// </summary>
public sealed class BusquedaRapidaResolver(
    IProcedureInstanceRepository repo,
    IChecklistCompanyParamsProvider? companyParams = null,
    IResolvedChecklistMatrixProvider? matrixProvider = null,
    IDocumentTypeCatalog? documentTypes = null,
    TimeProvider? clock = null)
{
    /// <summary>Máximo de borradores que se evalúan en memoria por consulta.</summary>
    public const int TopeBorradores = 1000;

    /// <summary>Carga una página de trámites con el grafo del listado (tenant propio o red).</summary>
    public delegate Task<(IReadOnlyList<ProcedureInstance> Items, int Total)> CargarCandidatos(
        ProcedureInstanceListFilter filter, int take, CancellationToken ct);

    /// <summary>
    /// Devuelve <paramref name="filter"/> con el atajo aplicado. Sin atajo lo devuelve tal cual.
    /// </summary>
    /// <exception cref="BusquedaRapidaDemasiadoAmpliaException">Más borradores que el tope.</exception>
    public async Task<ProcedureInstanceListFilter> AplicarAsync(
        ProcedureInstanceListFilter filter,
        string? atajo,
        CargarCandidatos cargar,
        Guid? usuarioActualId = null,
        CancellationToken ct = default)
    {
        var ahora = (clock ?? TimeProvider.System).GetUtcNow();
        switch (BusquedaRapida.Normalizar(atajo))
        {
            case null:
                return filter;
            case BusquedaRapida.MisTramites:
                // Sin usuario identificado no hay «míos»: ninguno, nunca «todos».
                return usuarioActualId is { } usuario
                    ? filter with { ResponsableId = usuario }
                    : filter with { IdsIncluidos = [] };
            case BusquedaRapida.MasDe5Dias:
                return filter with { EntregadoAntesDe = BusquedaRapida.CorteDeDias(5, ahora) };
            case BusquedaRapida.MasDe10Dias:
                return filter with { EntregadoAntesDe = BusquedaRapida.CorteDeDias(10, ahora) };
        }

        var atajoNormalizado = BusquedaRapida.Normalizar(atajo)!;
        var (candidatos, total) = await cargar(
            filter with { Estados = [TramiteEstado.Borrador] }, TopeBorradores, ct).ConfigureAwait(false);
        if (total > TopeBorradores)
            throw new BusquedaRapidaDemasiadoAmpliaException(total, TopeBorradores);

        var ids = new HashSet<Guid>();
        var quiereFirmas = atajoNormalizado is BusquedaRapida.SinFirmas or BusquedaRapida.Pausados;
        var quiereDocumentos = atajoNormalizado is BusquedaRapida.SinDocumento or BusquedaRapida.Pausados;

        if (quiereFirmas)
        {
            var filas = await ListProcedureInstancesFilteredHandler.ToSummariesAsync(repo, candidatos, ct)
                .ConfigureAwait(false);
            foreach (var fila in filas.Where(SinFirmar))
                ids.Add(fila.Id);
        }

        if (quiereDocumentos && companyParams is not null)
        {
            // Misma completitud que el gate de radicación, con las lecturas memorizadas durante esta
            // consulta: la matriz se repite por (tipo, organismo) y los parámetros por compañía.
            var completitud = new ChecklistMatrixCompleteness(
                new ParametrosMemorizados(companyParams),
                matrixProvider is null ? null : new MatrizMemorizada(matrixProvider),
                documentTypes is null ? null : new CatalogoMemorizado(documentTypes));
            foreach (var instancia in candidatos)
            {
                if (await completitud.TryComputeCompletoAsync(instancia, instancia.TenantId, ct).ConfigureAwait(false) == false)
                    ids.Add(instancia.Id);
            }
        }

        if (atajoNormalizado == BusquedaRapida.Pausados)
        {
            foreach (var instancia in candidatos.Where(i => i.IsPaused))
                ids.Add(instancia.Id);
        }

        return filter with { IdsIncluidos = ids };
    }

    /// <summary>Misma lectura que la etiqueta del actor: una parte convocada que no quedó «firmado».</summary>
    private static bool SinFirmar(InstanceSummaryDto fila) =>
        (fila.FirmaVendedorEstado is { } v && v != FirmaParteEstados.Firmado)
        || (fila.FirmaCompradorEstado is { } c && c != FirmaParteEstados.Firmado);

    private sealed class ParametrosMemorizados(IChecklistCompanyParamsProvider inner) : IChecklistCompanyParamsProvider
    {
        private readonly ConcurrentDictionary<Guid, Task<IReadOnlyList<CompanyDocumentParam>>> _cache = new();

        public Task<IReadOnlyList<CompanyDocumentParam>> GetForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            _cache.GetOrAdd(tenantId, id => inner.GetForTenantAsync(id, cancellationToken));
    }

    private sealed class MatrizMemorizada(IResolvedChecklistMatrixProvider inner) : IResolvedChecklistMatrixProvider
    {
        private readonly ConcurrentDictionary<(Guid, Guid?), Task<IReadOnlyList<ResolvedChecklistDoc>>> _cache = new();

        public Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(
            Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
            _cache.GetOrAdd((procedureTypeId, transitOfficeId), k => inner.GetForAsync(k.Item1, k.Item2, cancellationToken));
    }

    private sealed class CatalogoMemorizado(IDocumentTypeCatalog inner) : IDocumentTypeCatalog
    {
        private Task<IReadOnlySet<string>>? _generados;

        public Task<DocumentTypeRule?> GetRuleAsync(string tipo, CancellationToken ct = default) =>
            inner.GetRuleAsync(tipo, ct);

        public Task<IReadOnlySet<string>> ListSystemGeneratedCodesAsync(CancellationToken ct = default) =>
            _generados ??= inner.ListSystemGeneratedCodesAsync(ct);
    }
}
