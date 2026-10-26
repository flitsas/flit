using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerto del avalúo comercial (HU #13795, F05-C7): <c>ConsultasService.ConsultarAvaluos</c> de core-consultas, a nombre de la
/// empresa de la celda, que consulta en paralelo las fuentes habilitadas para la empresa (Fasecolda, MercadoLibre, base
/// gravable) y elige el valor sugerido con su fuente principal. Devuelve <see cref="ResultadoAvaluo"/> o el error tipado.
/// Ninguna fuente con valor NO es error: es <see cref="ResultadoAvaluo.SinValorComercial"/> (decisión Q4 = b). Implementaciones:
/// <c>FuenteAvaluoReal</c>, <see cref="FuenteAvaluoSimulada"/> (LOCAL o DEV) y <c>FuenteAvaluoNoConectada</c>,
/// elegidas por <c>SeleccionAdaptadores</c>. La usa <see cref="EjecutorCeldaAvaluo"/>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, solicitud, ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteAvaluo
{
    Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct);
}
