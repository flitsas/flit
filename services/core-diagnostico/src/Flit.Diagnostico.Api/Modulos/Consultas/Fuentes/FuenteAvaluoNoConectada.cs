namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Avalúo no conectado (HU #13795, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias; AVALUO tampoco se ofrece (422 FUENTE_NO_DISPONIBLE).
/// </summary>
public sealed class FuenteAvaluoNoConectada : IFuenteAvaluo
{
    public Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoAvaluo>.Fallo(ErroresFuentePersona.NoConectada("AVALUO")));
}
