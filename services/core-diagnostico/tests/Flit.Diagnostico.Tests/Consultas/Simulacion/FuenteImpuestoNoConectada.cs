using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// Doble de prueba de core-consultas (ADR-0075, U3 = a): la simulación de LOCAL y DEV vive en core-consultas; en las pruebas
/// responde los trabajos de <see cref="BusDePrueba"/>. Lo que sigue es su comportamiento heredado de la fuente de Diagnóstico.
/// Impuesto no conectado (HU #13862, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias; IMPUESTO_DEPARTAMENTAL tampoco se ofrece
/// (<c>SeleccionAdaptadores.EsNoConectada</c>, 422 FUENTE_NO_DISPONIBLE).
/// </summary>
public sealed class FuenteImpuestoNoConectada : IFuenteImpuesto
{
    public Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoImpuesto>.Fallo(ErroresFuentePersona.NoConectada("IMPUESTO_DEPARTAMENTAL")));
}
