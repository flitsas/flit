namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// SIMIT no conectado (HU #13793, regla de #13791): sin simulación ni dirección de core-consultas falla cerrado con
/// <c>NO_CONECTADO</c> (definitivo), nunca datos y sin dependencias; las fuentes SIMIT tampoco se ofrecen (422
/// FUENTE_NO_DISPONIBLE).
/// </summary>
public sealed class FuenteSimitNoConectada : IFuenteSimit
{
    public Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoSimit>.Fallo(ErroresFuentePersona.NoConectada("SIMIT")));
}
