using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13375 — procesamiento de ítem de los orígenes que entregan con el entregador común
/// (<see cref="ILoteItemEntregador"/>) sobre la compañía del trámite y revalidan con
/// <see cref="IConsolidadoLoteAccessChecker"/>: <c>tramites</c> y <c>superadmin</c>.
/// </summary>
/// <remarks>Uso de ejemplo: <c>services.AddScoped&lt;ILoteItemOrigen, TramitesLoteItemOrigen&gt;();</c>.</remarks>
public abstract class LoteItemOrigenEstandar(IConsolidadoLoteAccessChecker acceso, ILoteItemEntregador entregador)
    : ILoteItemOrigen
{
    public abstract string Origen { get; }

    public Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default) =>
        acceso.TieneAccesoAsync(contexto, ct);

    public Task<LoteItemEntregaResult> EntregarAsync(LoteItemContexto contexto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return entregador.EntregarAsync(
            new LoteItemEntregaRequest(
                contexto.ProcedureInstanceId,
                contexto.CompaniaTramiteId,
                contexto.TipoDocumento,
                AntesDeGenerar: EsDeOtraCompania(contexto) ? SinGenerarEnLaHija : null),
            ct);
    }

    /// <summary>
    /// HU #13418 (adenda v7, P1 = a) — el ítem es de una compañía distinta de la del lote (<c>item.tenant_id ≠
    /// batch.tenant_id</c>): el trámite de una hija en un lote de red. La red es de solo consulta: si hay que generar,
    /// se omite. El lote del Super Admin no tiene compañía y nunca entra aquí.
    /// </summary>
    private static bool EsDeOtraCompania(LoteItemContexto contexto) =>
        contexto.CompaniaLoteId is { } compania && compania != contexto.CompaniaTramiteId;

    /// <summary>
    /// Gancho del entregador: solo corre cuando NO hay consolidado guardado (nunca ante un existente), así que el ítem de
    /// la hija se entrega si ya tiene el documento y, si no, se omite sin crear adjunto, bitácora ni impronta en ella.
    /// </summary>
    private static Task<string?> SinGenerarEnLaHija(CancellationToken ct) =>
        Task.FromResult<string?>(ConsolidadoLoteOmisiones.RedSinConsolidado);
}

/// <summary>Origen <c>tramites</c>: listado de trámites de la compañía del Gestor/Radicador.</summary>
public sealed class TramitesLoteItemOrigen(IConsolidadoLoteAccessChecker acceso, ILoteItemEntregador entregador)
    : LoteItemOrigenEstandar(acceso, entregador)
{
    public override string Origen => ConsolidadoExportOrigin.Tramites;
}

/// <summary>Origen <c>superadmin</c>: lote del Super Admin (sin compañía en el lote; la del trámite en el ítem).</summary>
public sealed class SuperAdminLoteItemOrigen(IConsolidadoLoteAccessChecker acceso, ILoteItemEntregador entregador)
    : LoteItemOrigenEstandar(acceso, entregador)
{
    public override string Origen => ConsolidadoExportOrigin.Superadmin;
}
