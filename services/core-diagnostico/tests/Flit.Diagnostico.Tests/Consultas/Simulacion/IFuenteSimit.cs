using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerto de SIMIT (HU #13793, F05-C1, F05-C6, F05-C9): comparendos de una placa o de una persona con
/// <c>ConsultasService.ConsultarMultas</c> de core-consultas, a nombre de la empresa de la celda; el producto no elige proveedor
/// (lo decide core-consultas con la configuración de la empresa). Devuelve <see cref="ResultadoSimit"/> (semáforo, cantidad y
/// número, fecha, valor, estado y organismo de cada comparendo) o el error tipado. Implementaciones: <c>FuenteSimitReal</c>,
/// <see cref="FuenteSimitSimulada"/> (LOCAL o DEV) y <c>FuenteSimitNoConectada</c>, elegidas por
/// <c>SeleccionAdaptadores</c>. Lo usan <see cref="EjecutorCeldaSimitVehiculo"/> (por placa) y
/// <see cref="EjecutorCeldaSimitPersona"/> (HU #13794, por documento de cada persona).
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, SolicitudSimit.PorPlaca(placa), ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteSimit
{
    Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct);
}
