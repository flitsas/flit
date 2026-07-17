namespace Flit.Modules.Quipux.Application.UseCases.Cola;

/// <summary>Consulta de la cola Quipux de una secretaría destino (HU #10774). Paginada.</summary>
public sealed record ListarColaQuipuxQuery(Guid TransitOfficeId, int? Page = null, int? PageSize = null);
