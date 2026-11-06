using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// HU #14397 (Feature #14396, Épica #14297) — trámites del portal Persona Natural que cayeron a FLIT 2.0, por alcance.
///
/// <para><b>Qué entra.</b> Filas de <c>tramites.procedure_instances</c> con canal <c>pn_web</c>, <c>pn_punto</c> o
/// <c>aplicacion</c> (columnas de la Épica #14296). El canal <c>tramites</c> (gestor), <c>integracion</c> y el histórico sin canal
/// no están en el conjunto de canales, así que no pueden aparecer. Además, <c>pn_web</c>/<c>pn_punto</c> exigen la empresa
/// del portal (<see cref="PlatformTenants.PersonaNatural"/>): un trámite de otra empresa nunca se lee aquí, aunque llevara
/// el canal por error. Un borrador que FLIT 2 rechazó al crearlo se anula con
/// <see cref="MotivosAnulacionPortal.RechazoEnCreacion"/>: nunca cayó, y no se cuenta. Lo que se quedó a mitad del formulario
/// vive en el servicio del portal y no está en esta base.</para>
///
/// <para><b>De dónde sale el organismo.</b> <c>pn_punto</c>: <c>channel_transit_office_id</c>, que se graba al crear y no se mueve.
/// <c>pn_web</c> (y <c>aplicacion</c>): <c>transit_office_id</c>, el organismo que eligió la persona al radicar; FLIT 2 no guarda
/// un organismo de origen aparte para la web, así que si después el trámite se reasigna a otro organismo el reporte lo sigue.
/// Nunca se usa <c>transit_office_id</c> para un trámite del punto: ese sí cambia al enviarlo al OT.</para>
///
/// <para><b>Alcance.</b> Lo decide <see cref="OtTenantScope"/>, la única regla de qué organismo ve qué empresa: el administrador
/// del organismo recibe SU organismo del tenant del token y solo ve filas de empresas dentro de ese alcance. No hay otra
/// definición de «mi organismo» aquí.</para>
/// </summary>
internal sealed class ReportePortalReadRepository : IReportePortalReadRepository
{
    private readonly FlitDbContext _db;
    private readonly OtTenantScope _scope;

    public ReportePortalReadRepository(FlitDbContext db, IEffectiveTransitOfficeListResolver? effectiveOffices = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _scope = new OtTenantScope(db, effectiveOffices);
    }

    public async Task<(PaginaReportePortal? Pagina, string? Error)> ListarAsync(
        AlcanceReportePortal alcance, FiltroReportePortal filtro, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(filtro);

        PaginaReportePortal? pagina = null;
        var error = await EnAlcanceAsync(alcance, filtro, async consulta =>
        {
            var total = await consulta.CountAsync(ct).ConfigureAwait(false);
            var filas = total == 0
                ? []
                : await Proyectar(consulta).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
            pagina = new PaginaReportePortal([.. filas.Select(Mapear)], total, page, pageSize);
        }, ct).ConfigureAwait(false);

        return error is null ? (pagina, null) : (null, error);
    }

    public Task<string?> RecorrerAsync(
        AlcanceReportePortal alcance, FiltroReportePortal filtro, (int Page, int PageSize)? pagina,
        Func<FilaReportePortal, CancellationToken, Task> alFila, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(filtro);
        ArgumentNullException.ThrowIfNull(alFila);

        return EnAlcanceAsync(alcance, filtro, async consulta =>
        {
            var filas = Proyectar(consulta);
            if (pagina is { } p)
                filas = filas.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);

            await foreach (var fila in filas.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
                await alFila(Mapear(fila), ct).ConfigureAwait(false);
        }, ct);
    }

    /// <summary>
    /// Resuelve el organismo y las empresas que el alcance permite (vía <see cref="OtTenantScope"/>) y corre
    /// <paramref name="trabajo"/> con la consulta ya acotada. Devuelve el error de alcance o nulo.
    /// </summary>
    private async Task<string?> EnAlcanceAsync(
        AlcanceReportePortal alcance, FiltroReportePortal filtro, Func<IQueryable<ProcedureInstance>, Task> trabajo, CancellationToken ct)
    {
        if (alcance.Plataforma)
        {
            var organismos = Limpiar(alcance.Organismos);
            await _scope.ReadCrossTenantAsync(async () =>
            {
                await trabajo(Consulta(filtro, organismos, incluirPortal: true)).ConfigureAwait(false);
                return Hecho.Instancia;
            }, ct).ConfigureAwait(false);
            return null;
        }

        Guid organismoDelAlcance;
        if (alcance.OtTenantId is { } otTenant && otTenant != Guid.Empty)
        {
            var propio = await _scope.ResolveTransitOfficeIdAsync(otTenant, ct).ConfigureAwait(false);
            if (propio is null)
                return ReportePortalErrores.SinOrganismo;
            // Cualquier id ajeno dentro de la lista, aunque venga con el propio, rechaza todo: nada de otro organismo se lee.
            if (Limpiar(alcance.Organismos).Any(pedido => pedido != propio))
                return ReportePortalErrores.OtroOrganismo;
            organismoDelAlcance = propio.Value;
        }
        else if (Limpiar(alcance.Organismos) is [var indicado])
        {
            organismoDelAlcance = indicado;
        }
        else
        {
            return ReportePortalErrores.OrganismoRequerido;
        }

        var resultado = await _scope.ExecuteAsync(
            alcance.OtTenantId ?? Guid.Empty,
            organismoDelAlcance,
            async (organismo, empresas) =>
            {
                // La empresa del portal tiene que estar entre las que el organismo puede ver; si no, no hay nada que mostrar.
                await trabajo(Consulta(filtro, [organismo], incluirPortal: empresas.Contains(PlatformTenants.PersonaNatural)))
                    .ConfigureAwait(false);
                return Hecho.Instancia;
            },
            ct).ConfigureAwait(false);

        return resultado is null ? ReportePortalErrores.SinOrganismo : null;
    }

    private static Guid[] Limpiar(IReadOnlyList<Guid>? ids) => ids is null ? [] : [.. ids.Where(i => i != Guid.Empty).Distinct()];

    private IQueryable<ProcedureInstance> Consulta(FiltroReportePortal filtro, Guid[] organismos, bool incluirPortal)
    {
        var portal = PlatformTenants.PersonaNatural;
        var canales = incluirPortal
            ? filtro.Canales
            : [.. filtro.Canales.Where(c => c == CanalesReportePortal.Aplicacion)];

        var q = _db.ProcedureInstances.AsNoTracking()
            .Where(p => p.DeletedAt == null
                && p.Channel != null && canales.Contains(p.Channel)
                // El portal vive en su empresa; solo «aplicacion» (sin dueño definido todavía) no la exige.
                && (p.Channel == TramiteCanal.Aplicacion || p.TenantId == portal)
                // Rechazado al crearse: el borrador quedó anulado y nunca cayó a FLIT 2.0.
                && !(p.Status == TramiteEstado.Anulado
                     && _db.ProcedureInstanceStatusHistories.Any(h => h.ProcedureInstanceId == p.Id
                        && h.ToStatus == TramiteEstado.Anulado
                        && h.Reason != null && h.Reason.StartsWith(MotivosAnulacionPortal.RechazoEnCreacion))));

        if (organismos.Length > 0)
        {
            // pn_punto: el organismo del punto (no cambia). Los demás: el del trámite.
            q = q.Where(p => (p.Channel == TramiteCanal.PersonaNaturalOt && p.ChannelTransitOfficeId != null && organismos.Contains(p.ChannelTransitOfficeId.Value))
                || (p.Channel != TramiteCanal.PersonaNaturalOt && p.TransitOfficeId != null && organismos.Contains(p.TransitOfficeId.Value)));
        }

        if (filtro.Tipos.Count > 0)
        {
            var tipos = filtro.Tipos.ToArray();
            q = q.Where(p => tipos.Contains(p.ProcedureTypeId));
        }

        if (filtro.Desde is { } desde)
        {
            var from = OtTenantScope.DayRange(desde, desde).From;
            q = q.Where(p => p.CreatedAt >= from);
        }

        if (filtro.Hasta is { } hasta)
        {
            var to = OtTenantScope.DayRange(hasta, hasta).To;
            q = q.Where(p => p.CreatedAt <= to);
        }

        return q;
    }

    private IQueryable<FilaCruda> Proyectar(IQueryable<ProcedureInstance> consulta) =>
        consulta
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Select(p => new FilaCruda(
                p.Channel!,
                _db.TransitOffices
                    .Where(o => o.Id == (p.Channel == TramiteCanal.PersonaNaturalOt ? p.ChannelTransitOfficeId : p.TransitOfficeId))
                    .Select(o => o.Name)
                    .FirstOrDefault(),
                p.ProcedureType!.Name,
                p.ReferenceNumber,
                p.Status,
                p.CreatedAt));

    // Modalidad y valores: nulos hasta que exista el pago. No se calculan ni se infieren.
    private static FilaReportePortal Mapear(FilaCruda f) => new(
        f.Canal, f.Organismo, f.Tipo, string.IsNullOrWhiteSpace(f.Radicado) ? null : f.Radicado, f.Estado, f.CreatedAt,
        Modalidad: null, ValorTramite: null, ValorGestion: null);

    private sealed record FilaCruda(string Canal, string? Organismo, string Tipo, string Radicado, string Estado, DateTimeOffset CreatedAt);

    private sealed class Hecho
    {
        public static readonly Hecho Instancia = new();
    }
}
