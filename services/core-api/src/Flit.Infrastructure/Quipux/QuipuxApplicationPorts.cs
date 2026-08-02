using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Application.UseCases.EncolarEnvio;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Asegura el PDF consolidado maestro delegando en <see cref="GenerarConsolidadoMaestroHandler"/>.
/// </summary>
/// <remarks>
/// <para>Este adaptador existe para que <c>Flit.Modules.Quipux.Application</c> NO dependa de
/// <c>Flit.Tramites.Application</c>: el módulo declara el puerto y aquí, en Infrastructure —que ya
/// referencia todo—, se une con el handler real. Sin él, un módulo Application dependería de otro,
/// que es justo lo que la separación por módulos evita.</para>
/// <para>No se reimplementa nada de PDF: el handler de trámites ya resuelve el orden por la matriz
/// documental y, cuando <c>ProcedureInstance.ConsolidadoMaestroVigente</c> está en true y el
/// adjunto existe, lo reutiliza sin regenerar. Esa vigencia es el equivalente —mejor resuelto— del
/// chequeo <c>dateSentOt == hoy</c> de FLIT 1.0, que servía para no reconstruir el PDF en cada
/// reintento del mismo día.</para>
/// </remarks>
internal sealed class QuipuxConsolidadoMaestroAdapter(GenerarConsolidadoMaestroHandler handler)
    : IQuipuxConsolidadoMaestroPort
{
    public async Task<QuipuxConsolidadoMaestro> AsegurarAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // HU #11184 (AC5) — `matrizPrecedencia` sigue en null a propósito: es el parámetro que
        // compone la consola OT con la matriz del checklist. El orden que el organismo configuró lo
        // resuelve ahora el propio handler (IOtConfiguredDocumentOrderProvider), así que el envío
        // por el canal de radicación sale con la misma prelación que ve en su pantalla.
        var (result, error) = await handler
            .HandleAsync(procedureInstanceId, tenantId, matrizPrecedencia: null, cancellationToken)
            .ConfigureAwait(false);

        if (error is not null || result is null)
        {
            // Se propaga el código del handler tal cual (not_found, sin_adjuntos,
            // adjunto_no_disponible, mimetype_no_soportado): el llamador decide si es reintentable.
            return QuipuxConsolidadoMaestro.Fallo(error ?? "consolidado_maestro_sin_resultado");
        }

        return result.Regenerado
            ? QuipuxConsolidadoMaestro.Generado(result.Document.AttachmentId)
            : QuipuxConsolidadoMaestro.Reutilizado(result.Document.AttachmentId);
    }
}

/// <summary>
/// Lee el código DIVIPO del organismo desde <c>catalogs.transit_offices.code_divipo</c>.
/// </summary>
/// <remarks>
/// Es una columna y no una clave de <c>external_refs</c> porque el DIVIPO se carga A MANO,
/// secretaría por secretaría (solo se conocen 6 de 317): un jsonb no valida el formato ni delata un
/// typo en la clave, que dejaría a la secretaría fuera del gate en silencio, y una columna se
/// expone además en la consola de administración. Null = desconocido = no elegible.
/// </remarks>
internal sealed class QuipuxOrganismoAdapter(FlitDbContext db) : IQuipuxOrganismoPort
{
    public async Task<string?> ObtenerCodigoDivipoAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var codigo = await db.TransitOffices
            .AsNoTracking()
            .Where(o => o.Id == transitOfficeId && o.IsActive)
            .Select(o => o.DivipoCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(codigo) ? null : codigo;
    }
}

/// <summary>
/// Lee la razón social del cliente (<c>identity.tenants.legal_name</c>), que es la primera parte
/// del <c>document_name</c> que ve la secretaría.
/// </summary>
internal sealed class QuipuxTenantAdapter(FlitDbContext db) : IQuipuxTenantPort
{
    public async Task<string?> ObtenerRazonSocialAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.LegalName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
