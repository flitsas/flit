using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Infrastructure.ConsolidadoLotes.Ot;

/// <summary>
/// Épica #13216 (HU #13390, ADR-0070 A5.6 R-a) — carga del filtro de «Seleccionar todos» en la bandeja del
/// OT: los mismos criterios que <c>POST /api/v1/admin/ot/client-procedures/search</c>
/// (<c>OtBandejaSearchRequest.ToFilter()</c>). <c>Page</c> y <c>PageSize</c> de <see cref="Criterios"/> se
/// IGNORAN: la selección no tiene página. El tenant OT y el organismo salen de
/// <see cref="LoteSeleccionContexto"/>, nunca del cuerpo.
/// </summary>
/// <remarks>
/// Vive en Infrastructure porque <c>Flit.Tramites.Application</c> (contrato del motor) y
/// <c>Flit.Admin.Domain</c> (filtro de la bandeja) no se referencian entre sí.
/// <para>HU #13373 — declara su resumen minimizado para <c>filter_summary</c> (<see cref="ILoteFiltroResumible"/>):
/// texto libre como <c>{presente, longitud}</c> y condiciones como <c>{campo, operador, cantidad}</c>.</para>
/// <para>HU #13390 (L3, Habeas Data) — <c>status</c>, <c>familia</c>, <c>sortBy</c> y <c>sortDir</c> solo quedan
/// literales si pertenecen a su catálogo; si no, como <c>{presente, longitud}</c>. <c>filter_summary</c> es
/// append-only: un campo «de catálogo» sin validar dejaba texto libre (nombres, documentos) para siempre. El resumen
/// no cambia qué trámites entran al lote.</para>
/// </remarks>
public sealed record OtBandejaLoteFiltro(OtClientProcedureFilter Criterios) : LoteFiltro, ILoteFiltroResumible
{
    public string OrigenFiltro => Flit.Tramites.Domain.Entities.ConsolidadoLotes.ConsolidadoExportOrigin.OtBandeja;

    public void Resumir(LoteFiltroResumen resumen)
    {
        ArgumentNullException.ThrowIfNull(resumen);
        var c = Criterios;
        resumen.TextoLibre("busqueda", c.Busqueda)
            .TextoLibre("vin", c.Vin)
            .TextoLibre("placa", c.Placa)
            .TextoLibre("vendedor", c.Vendedor)
            .TextoLibre("comprador", c.Comprador)
            .TextoLibre("gestor", c.Gestor)
            .Catalogos("status", Estados(c.Status), EstadoCanonico)
            .Booleano("hasActiveRevocationRequest", c.HasActiveRevocationRequest)
            .Identificador("procedureTypeId", c.ProcedureTypeId)
            .Catalogo("familia", c.Familia, FamiliaCanonica)
            .Fecha("createdFrom", c.CreatedFrom)
            .Fecha("createdTo", c.CreatedTo)
            .Fecha("updatedFrom", c.UpdatedFrom)
            .Fecha("updatedTo", c.UpdatedTo)
            .Condiciones(c.Condiciones)
            .Catalogo("sortBy", c.SortBy, OtClientProcedureListSort.Canonico)
            .Catalogo("sortDir", c.SortDir, OtClientProcedureListSort.DireccionCanonica);
    }

    /// <summary>
    /// Los mismos trozos que filtra el repositorio (<c>Split(',')</c> sin vacíos y recortados), en su orden.
    /// </summary>
    private static string[]? Estados(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? null
            : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// La bandeja del OT compara el estado real en minúsculas (<c>p.Status == e</c>): el catálogo es
    /// <see cref="TramiteEstado.Todos"/>, sin el pseudo-estado del listado del gestor (ADR-0059).
    /// </summary>
    private static string? EstadoCanonico(string valor)
    {
        var e = valor.ToLowerInvariant();
        return TramiteEstado.EsValido(e) ? e : null;
    }

    /// <summary>Familia por el único parser del vocabulario (ADR-0050), como en la rama <c>tramites</c>.</summary>
    private static string? FamiliaCanonica(string valor) =>
        ProcedureFamilyCodes.FromCode(valor) is { } familia ? ProcedureFamilyCodes.ToCode(familia) : null;
}
