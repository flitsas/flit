using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Infrastructure.ConsolidadoLotes.Ot;

/// <summary>
/// Épica #13216 (HU #13390, ADR-0070 adenda v4 A4.1) — selección del lote desde la bandeja del OT (origen
/// <see cref="ConsolidadoExportOrigin.OtBandeja"/>). No tiene lógica de visibilidad propia: todo pasa por
/// <see cref="IOtClientProcedureRepository.ListAccessibleRefsAsync"/>, el mismo universo, filtros y orden que
/// la bandeja (organismo, recibido y vivo; el grant no la gobierna, HU #12350).
/// <list type="bullet">
///   <item>Modo filtro: todo lo que la bandeja muestra con el filtro, sin tope ni página, menos los excluidos.</item>
///   <item>Modo ids: las casillas marcadas ∩ la bandeja, sin filtros. Un id de otro organismo, en borrador,
///   borrado o inexistente simplemente no aparece.</item>
/// </list>
/// Cada referencia lleva como compañía la cliente dueña del trámite, nunca el tenant OT. Los topes de 10.000
/// ids/excluidos (Q7) los aplica la creación del lote (#13373/#13391), no este resolver.
/// </summary>
internal sealed class OtBandejaSeleccionResolver(IOtClientProcedureRepository repository) : ILoteSeleccionResolver
{
    public string Origen => ConsolidadoExportOrigin.OtBandeja;

    public async Task<IReadOnlyList<ProcedureInstanceRef>> ResolverAsync(
        LoteSeleccion seleccion, LoteSeleccionContexto contexto, int? limite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        ArgumentNullException.ThrowIfNull(contexto);
        var otTenantId = TenantOt(contexto);

        var refs = seleccion switch
        {
            SeleccionPorIds porIds => await ResolverIdsAsync(porIds, otTenantId, contexto, limite, ct).ConfigureAwait(false),
            SeleccionPorFiltro porFiltro => await ResolverFiltroAsync(porFiltro, otTenantId, contexto, limite, ct).ConfigureAwait(false),
            _ => throw NoSoportado(seleccion),
        };

        return refs
            .Select(r => new ProcedureInstanceRef(r.Id, r.ClientTenantId, r.ReferenceNumber, r.Plate))
            .ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Code review épica #13216 (Obs2) — <see cref="IOtClientProcedureRepository.CountAccessibleRefsAsync"/> con el
    /// mismo universo y filtro; en modo filtro se resta el conteo de los excluidos que están en la bandeja con ese
    /// filtro (mismo predicado + ids = excluidos), así un excluido ajeno no resta.
    /// </remarks>
    public async Task<int> ContarAsync(LoteSeleccion seleccion, LoteSeleccionContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        ArgumentNullException.ThrowIfNull(contexto);
        var otTenantId = TenantOt(contexto);

        switch (seleccion)
        {
            case SeleccionPorIds porIds:
                {
                    var ids = (porIds.Ids ?? []).Distinct().ToList();
                    return ids.Count == 0
                        ? 0
                        : await repository.CountAccessibleRefsAsync(otTenantId, filter: null, ids, contexto.OtTransitOfficeId, ct)
                            .ConfigureAwait(false);
                }

            case SeleccionPorFiltro porFiltro:
                {
                    var criterios = CriteriosValidados(porFiltro);
                    var total = await repository.CountAccessibleRefsAsync(
                        otTenantId, criterios, ids: null, contexto.OtTransitOfficeId, ct).ConfigureAwait(false);
                    var excluidos = (porFiltro.Excluidos ?? []).Distinct().ToList();
                    if (excluidos.Count == 0 || total == 0)
                        return total;

                    var excluidosEnLaBandeja = await repository.CountAccessibleRefsAsync(
                        otTenantId, criterios, excluidos, contexto.OtTransitOfficeId, ct).ConfigureAwait(false);
                    return Math.Max(0, total - excluidosEnLaBandeja);
                }

            default:
                throw NoSoportado(seleccion);
        }
    }

    private async Task<IReadOnlyList<OtClientProcedureRef>> ResolverIdsAsync(
        SeleccionPorIds seleccion, Guid otTenantId, LoteSeleccionContexto contexto, int? limite, CancellationToken ct)
    {
        var ids = (seleccion.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await repository.ListAccessibleRefsAsync(
            otTenantId, filter: null, ids, contexto.OtTransitOfficeId, limite, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OtClientProcedureRef>> ResolverFiltroAsync(
        SeleccionPorFiltro seleccion, Guid otTenantId, LoteSeleccionContexto contexto, int? limite, CancellationToken ct)
    {
        var criterios = CriteriosValidados(seleccion);
        var excluidos = seleccion.Excluidos ?? [];

        // Obs2: los excluidos se restan en memoria, así que se leen limite + excluidos (contrato de
        // ILoteSeleccionResolver: con la selección >= limite, quedan al menos `limite`).
        var refs = await repository.ListAccessibleRefsAsync(
            otTenantId, criterios, ids: null, contexto.OtTransitOfficeId, limite is { } max ? max + excluidos.Count : null, ct)
            .ConfigureAwait(false);

        if (excluidos.Count == 0)
            return refs;

        var fuera = excluidos.ToHashSet();
        return refs.Where(r => !fuera.Contains(r.Id)).ToList();
    }

    /// <summary>
    /// El lote OT siempre tiene tenant OT (D-FB6: también el del Super Admin, ya resuelto). Sin él no hay universo de
    /// bandeja que acotar: falla cerrado en vez de leer nada cross-tenant.
    /// </summary>
    private Guid TenantOt(LoteSeleccionContexto contexto) =>
        contexto.TenantId is Guid otTenantId && otTenantId != Guid.Empty
            ? otTenantId
            : throw new ArgumentException(
                $"El origen '{Origen}' exige el tenant del organismo de tránsito en el contexto.", nameof(contexto));

    /// <summary>
    /// Mismas validaciones que POST /client-procedures/search: un campo o una familia fuera de catálogo no se ignora,
    /// porque devolvería MÁS trámites de los que el usuario filtró.
    /// </summary>
    private OtClientProcedureFilter CriteriosValidados(SeleccionPorFiltro seleccion)
    {
        if (seleccion.Filtro is not OtBandejaLoteFiltro filtro)
            throw new ArgumentException(
                $"El origen '{Origen}' no acepta el filtro {seleccion.Filtro?.GetType().Name ?? "null"}.",
                nameof(seleccion));

        if (OtBandejaQueryConditions.Validate(filtro.Criterios.Condiciones) is { } problema)
            throw new LoteSeleccionInvalidaException(LoteSeleccionInvalidaException.CodigoFiltroInvalido, problema);
        if (!string.IsNullOrWhiteSpace(filtro.Criterios.Familia) && !ProcedureFamilyCodes.IsValid(filtro.Criterios.Familia))
            throw new LoteSeleccionInvalidaException(
                LoteSeleccionInvalidaException.CodigoFiltroInvalido,
                $"Familia no válida: '{filtro.Criterios.Familia}'. Valores permitidos: {string.Join(", ", ProcedureFamilyCodes.All)}.");

        return filtro.Criterios;
    }

    private static ArgumentException NoSoportado(LoteSeleccion seleccion) =>
        new($"Modo de selección no soportado: {seleccion.GetType().Name}.", nameof(seleccion));
}
