namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Una licencia de conducción normalizada: categoría, estado y vigencia (AAAA-MM-DD si el proveedor la informa).</summary>
public sealed record LicenciaConduccion(string Categoria, string? Estado, string? VigenteHasta);

/// <summary>
/// Resultado normalizado del RUNT persona (HU #13792, F05-C4): si la persona está inscrita, el estado de conductor y sus
/// licencias. Sin nombres, sin documento y sin multas (eso es SIMIT, F05-H5).
/// </summary>
public sealed record ResultadoPersonaRunt(bool Inscrito, string? EstadoConductor, IReadOnlyList<LicenciaConduccion> Licencias);

/// <summary>
/// Puerto del RUNT persona (HU #13792, F05-C4, R-24): consulta a una persona natural por tipo y número de documento con
/// <c>ConsultasService.ConsultarConductor</c> de core-consultas, a nombre de la empresa de la celda. Implementaciones:
/// <see cref="FuenteRuntPersonaReal"/>, <see cref="FuenteRuntPersonaSimulada"/> (LOCAL o DEV) y
/// <see cref="FuenteRuntPersonaNoConectada"/>, elegidas por <see cref="SeleccionAdaptadores"/>.
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
