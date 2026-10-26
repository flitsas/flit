using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Puerto del RUNT persona (HU #13792, F05-C4, R-24): consulta a una persona natural por tipo y número de documento con
/// <c>ConsultasService.ConsultarConductor</c> de core-consultas, a nombre de la empresa de la celda. Implementaciones:
/// <c>FuenteRuntPersonaReal</c>, <see cref="FuenteRuntPersonaSimulada"/> (LOCAL o DEV) y
/// <c>FuenteRuntPersonaNoConectada</c>, elegidas por <c>SeleccionAdaptadores</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, new DocumentoPersona("CC", numero), ct);
/// </code>
/// </example>
public interface IFuenteRuntPersona
{
    Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct);
}
