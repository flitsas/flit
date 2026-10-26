using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerto del impuesto vehicular departamental (HU #13862, F09-C5, F09-C6): <c>ConsultasService.ConsultarImpuestoVehicular</c>
/// de core-consultas a nombre de la empresa de la celda. Devuelve <see cref="ResultadoImpuesto"/> o el error tipado, con el
/// proveedor que respondió y su transacción solo para el intento auditable (R-20: nunca al payload). Implementaciones:
/// <c>FuenteImpuestoReal</c>, <see cref="FuenteImpuestoSimulada"/> (LOCAL o DEV) y <see cref="FuenteImpuestoNoConectada"/>,
/// elegidas por <c>SeleccionAdaptadores</c>. La usa <see cref="EjecutorCeldaImpuesto"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, new SolicitudImpuesto(placa, departamento), ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteImpuesto
{
    Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct);
}
