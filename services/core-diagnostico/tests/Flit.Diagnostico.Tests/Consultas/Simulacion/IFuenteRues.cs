using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerto del RUES (HU #13792, decisión Q2): la persona con NIT se consulta por <c>ConsultasService.ConsultarRues</c> de
/// core-consultas (proveedor <c>verifik_rues</c>), con el NIT sin dígito de verificación y a nombre de la empresa de la celda.
/// Implementaciones: <c>FuenteRuesReal</c>, <see cref="FuenteRuesSimulada"/> (LOCAL o DEV) y
/// <c>FuenteRuesNoConectada</c>, elegidas por <c>SeleccionAdaptadores</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, "999990305", ct);
/// </code>
/// </example>
public interface IFuenteRues
{
    Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct);
}
