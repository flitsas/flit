using Flit.Admin.Domain.OtClientProcedures;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;

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
            .Literal("status", c.Status)
            .Booleano("hasActiveRevocationRequest", c.HasActiveRevocationRequest)
            .Identificador("procedureTypeId", c.ProcedureTypeId)
            .Literal("familia", c.Familia)
            .Fecha("createdFrom", c.CreatedFrom)
            .Fecha("createdTo", c.CreatedTo)
            .Fecha("updatedFrom", c.UpdatedFrom)
            .Fecha("updatedTo", c.UpdatedTo)
            .Condiciones(c.Condiciones)
            .Literal("sortBy", c.SortBy)
            .Literal("sortDir", c.SortDir);
    }
}
