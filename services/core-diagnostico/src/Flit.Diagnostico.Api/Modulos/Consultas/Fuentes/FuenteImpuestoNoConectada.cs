namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Impuesto no conectado (HU #13862, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias; IMPUESTO_DEPARTAMENTAL tampoco se ofrece
/// (<see cref="SeleccionAdaptadores.EsNoConectada"/>, 422 FUENTE_NO_DISPONIBLE).
/// </summary>
public sealed class FuenteImpuestoNoConectada : IFuenteImpuesto
{
    public Task<RespuestaFuente<ResultadoImpuesto>> ConsultarAsync(Guid empresa, SolicitudImpuesto solicitud, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoImpuesto>.Fallo(ErroresFuentePersona.NoConectada("IMPUESTO_DEPARTAMENTAL")));
}
