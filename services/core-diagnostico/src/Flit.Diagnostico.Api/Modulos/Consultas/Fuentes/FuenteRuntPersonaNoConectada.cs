namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// RUNT persona no conectado (HU #13792, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias; la fuente tampoco se ofrece (422 FUENTE_NO_DISPONIBLE).
/// </summary>
public sealed class FuenteRuntPersonaNoConectada : IFuenteRuntPersona
{
    public Task<RespuestaFuente<ResultadoPersonaRunt>> ConsultarAsync(Guid empresa, DocumentoPersona documento, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoPersonaRunt>.Fallo(ErroresFuentePersona.NoConectada("RUNT persona")));
}
