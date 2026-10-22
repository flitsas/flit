namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUES no conectado (HU #13792, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias.
/// </summary>
public sealed class FuenteRuesNoConectada : IFuenteRues
{
    public Task<RespuestaFuente<ResultadoRues>> ConsultarAsync(Guid empresa, string nit, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoRues>.Fallo(ErroresFuentePersona.NoConectada("RUES")));
}
