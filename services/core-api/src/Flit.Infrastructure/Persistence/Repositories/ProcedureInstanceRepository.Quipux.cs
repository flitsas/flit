using Flit.Modules.Quipux.Domain.Envios;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Marca «Quipux» del listado de trámites: en qué punto del canal Quipux va cada fila de la página.
/// </summary>
internal sealed partial class ProcedureInstanceRepository
{
    /// <summary>
    /// Último envío por trámite (más reciente por <c>created_at DESC, id DESC</c>) o, sin envíos, <c>en_cola</c> si el trámite
    /// está <c>preparado</c> y su OT destino tiene canal Quipux activo para la familia del tipo: el MISMO predicado que usa
    /// <c>QuipuxSubmissionRepository.ListElegiblesSinSubmissionAsync</c> (tipo con <c>external_refs-&gt;'quipux'</c>, OT activo
    /// con DIVIPO, bandera por familia) más la integración encendida (<c>admin.quipux_settings.enabled</c>). SQL crudo porque
    /// <c>external_refs</c> no está mapeado en EF. Solo las filas de la página (<c>ANY(@ids)</c>), sin N+1.
    /// </summary>
    private const string QuipuxEstadosSql = $$"""
        -- Alias en snake_case: SqlQueryRaw<T> mapea por la convención del DbContext (Id→id, Estado→estado).
        SELECT pi.id AS id,
               CASE
                 WHEN ls.status = '{{QuipuxSubmissionEstado.Registrado}}' THEN '{{QuipuxListadoEstado.Radicado}}'
                 -- «Falló» solo mientras el trámite sigue esperando en preparado: si se movió por otra vía
                 -- (a mano, anulado, devuelto a borrador) el fallo es historia y la marca confundiría.
                 WHEN ls.status = '{{QuipuxSubmissionEstado.Fallido}}'
                      AND pi.status = '{{TramiteEstado.Preparado}}'                 THEN '{{QuipuxListadoEstado.Fallido}}'
                 WHEN ls.status = '{{QuipuxSubmissionEstado.Rechazado}}'  THEN '{{QuipuxListadoEstado.Rechazado}}'
                 WHEN ls.status = '{{QuipuxSubmissionEstado.Aprobado}}'   THEN '{{QuipuxListadoEstado.Aprobado}}'
                 WHEN (ls.status IS NULL OR ls.status = '{{QuipuxSubmissionEstado.Pendiente}}')
                      AND pi.status = '{{TramiteEstado.Preparado}}'
                      AND pt.external_refs -> 'quipux' IS NOT NULL
                      AND o.is_active
                      AND NULLIF(o.divipo_code, '') IS NOT NULL
                      AND CASE pt.external_refs -> 'quipux' ->> 'familia'
                            WHEN 'MATRICULA' THEN o.quipux_registration
                            WHEN 'TRASPASO'  THEN o.quipux_transfer
                            WHEN 'OTROS'     THEN o.quipux_other
                            ELSE false
                          END
                      AND EXISTS (SELECT 1 FROM admin.quipux_settings qs WHERE qs.enabled)
                   THEN '{{QuipuxListadoEstado.EnCola}}'
               END AS estado,
               ls.id AS envio_id
        FROM tramites.procedure_instances pi
        JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
        LEFT JOIN catalogs.transit_offices o ON o.id = pi.transit_office_id
        LEFT JOIN LATERAL (
            SELECT s.id, s.status
            FROM tramites.quipux_submissions s
            WHERE s.procedure_instance_id = pi.id
            ORDER BY s.created_at DESC, s.id DESC
            LIMIT 1) ls ON true
        WHERE pi.id = ANY({0}::uuid[])
        """;

    public async Task<IReadOnlyDictionary<Guid, QuipuxMarcaListado>> GetQuipuxEstadosAsync(
        IReadOnlyCollection<Guid> instanceIds, CancellationToken ct = default)
    {
        if (instanceIds.Count == 0 || !db.Database.IsRelational())
            return new Dictionary<Guid, QuipuxMarcaListado>();

        var rows = await db.Database
            .SqlQueryRaw<QuipuxEstadoSqlRow>(QuipuxEstadosSql, instanceIds.Distinct().ToArray())
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Where(r => r.Estado is not null)
            .ToDictionary(r => r.Id, r => new QuipuxMarcaListado(r.Estado!, r.EnvioId));
    }

    private sealed class QuipuxEstadoSqlRow
    {
        public Guid Id { get; init; }

        public string? Estado { get; init; }

        public Guid? EnvioId { get; init; }
    }
}
