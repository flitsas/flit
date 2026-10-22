namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Resultado normalizado del RUES (HU #13792, F05-C9, decisión Q2): si la matrícula mercantil apareció, su estado y la última
/// renovación (AAAA-MM-DD, o el año si el proveedor solo informa el último año renovado). Nunca la razón social, el NIT, los
/// representantes ni la dirección.
/// </summary>
public sealed record ResultadoRues(bool Encontrada, string? EstadoMatricula, string? UltimaRenovacion);

/// <summary>
/// Puerto del RUES (HU #13792, decisión Q2): la persona con NIT se consulta por <c>ConsultasService.ConsultarRues</c> de
/// core-consultas (proveedor <c>verifik_rues</c>), con el NIT sin dígito de verificación y a nombre de la empresa de la celda.
/// Implementaciones: <see cref="FuenteRuesReal"/>, <see cref="FuenteRuesSimulada"/> (LOCAL o DEV) y
/// <see cref="FuenteRuesNoConectada"/>, elegidas por <see cref="SeleccionAdaptadores"/>.
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
