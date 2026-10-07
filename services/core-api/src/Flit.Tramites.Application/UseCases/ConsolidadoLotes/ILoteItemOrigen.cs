using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13375 (Épica #13216, ADR-0070 D3) — punto de extensión por origen del lote para procesar UN ítem:
/// cómo se revalida el acceso del solicitante (CF-16) y con qué contexto se entrega el PDF. Los orígenes
/// <c>tramites</c> y <c>superadmin</c> los atiende <see cref="LoteItemOrigenEstandar"/>; la bandeja del OT
/// (<c>ot_bandeja</c>) registra el suyo en la HU #13392.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var origen = porOrigen.Para(lote.Origin);
/// var ctx = LoteItemContexto.Desde(lote, item);
/// if (await origen.TieneAccesoAsync(ctx, ct)) { var r = await origen.EntregarAsync(ctx, ct); }
/// </code>
/// </remarks>
public interface ILoteItemOrigen
{
    /// <summary>Clave <c>origin</c> de <c>consolidado_export_batches</c> que atiende.</summary>
    string Origen { get; }

    /// <summary>
    /// ¿El solicitante del lote conserva acceso a este trámite? <c>false</c> ⇒ el ítem se omite con
    /// <c>acceso_revocado</c> sin llamar al entregador. Una excepción se trata como fallo técnico (reintento).
    /// </summary>
    Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default);

    /// <summary>Entrega el PDF del trámite con el contexto del origen (compañía, organismo, matriz…).</summary>
    Task<LoteItemEntregaResult> EntregarAsync(LoteItemContexto contexto, CancellationToken ct = default);
}

/// <summary>
/// Contexto de un ítem del lote (AC6 de la HU #13375): origen, compañía congelada del lote, compañía del
/// trámite y organismo. Se arma SOLO con lo congelado en el lote y en el ítem, nunca con la sesión.
/// </summary>
/// <param name="BatchId">Lote.</param>
/// <param name="ItemId">Ítem.</param>
/// <param name="Origen">Uno de <see cref="ConsolidadoExportOrigin"/>.</param>
/// <param name="CompaniaLoteId">
/// Compañía congelada del lote (<c>batch.tenant_id</c>); <c>null</c> solo en el lote de Super Admin (E5).
/// </param>
/// <param name="CompaniaTramiteId">Compañía del trámite congelada en el ítem (<c>item.tenant_id</c>, CF-15).</param>
/// <param name="OrganismoId">Organismo de tránsito del lote (<c>ot_transit_office_id</c>); solo en <c>ot_bandeja</c>.</param>
/// <param name="SolicitanteId">Usuario dueño del lote.</param>
/// <param name="RolSolicitante">Rol con el que se creó el lote.</param>
/// <param name="ProcedureInstanceId">Trámite del ítem.</param>
/// <param name="TipoDocumento"><c>consolidado</c> o <c>consolidado_maestro</c>.</param>
public sealed record LoteItemContexto(
    Guid BatchId,
    Guid ItemId,
    string Origen,
    Guid? CompaniaLoteId,
    Guid CompaniaTramiteId,
    Guid? OrganismoId,
    Guid SolicitanteId,
    string RolSolicitante,
    Guid ProcedureInstanceId,
    string TipoDocumento)
{
    /// <summary>
    /// Compañía contra la que se revalida: la del lote; en el lote de Super Admin (sin compañía) la del trámite.
    /// </summary>
    public Guid CompaniaCongelada => CompaniaLoteId ?? CompaniaTramiteId;

    /// <summary>Contexto a partir del lote y su ítem.</summary>
    /// <exception cref="ArgumentException">El ítem no pertenece al lote.</exception>
    public static LoteItemContexto Desde(ConsolidadoExportBatch lote, ConsolidadoExportBatchItem item)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(item);
        if (item.BatchId != lote.Id)
            throw new ArgumentException("El ítem no pertenece al lote.", nameof(item));

        return new LoteItemContexto(
            lote.Id,
            item.Id,
            lote.Origin,
            lote.TenantId,
            item.TenantId,
            lote.OtTransitOfficeId,
            lote.RequestedByUserId,
            lote.RequestedRoleCode,
            item.ProcedureInstanceId,
            lote.DocumentType);
    }
}

/// <summary>Elige el <see cref="ILoteItemOrigen"/> por la clave <c>origin</c> del lote.</summary>
public sealed class LoteItemOrigenPorOrigen
{
    private readonly Dictionary<string, ILoteItemOrigen> _porOrigen;

    public LoteItemOrigenPorOrigen(IEnumerable<ILoteItemOrigen> origenes)
    {
        _porOrigen = new Dictionary<string, ILoteItemOrigen>(StringComparer.Ordinal);
        foreach (var origen in origenes)
        {
            if (!_porOrigen.TryAdd(origen.Origen, origen))
                throw new InvalidOperationException($"Hay dos procesadores de ítem para el origen '{origen.Origen}'.");
        }
    }

    /// <exception cref="InvalidOperationException">No hay procesador registrado para el origen.</exception>
    public ILoteItemOrigen Para(string origen) =>
        _porOrigen.TryGetValue(origen, out var procesador)
            ? procesador
            : throw new InvalidOperationException($"No hay procesador de ítem para el origen '{origen}'.");
}
