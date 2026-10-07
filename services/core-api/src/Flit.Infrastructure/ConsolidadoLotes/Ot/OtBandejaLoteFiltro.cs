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
/// </remarks>
public sealed record OtBandejaLoteFiltro(OtClientProcedureFilter Criterios) : LoteFiltro;
