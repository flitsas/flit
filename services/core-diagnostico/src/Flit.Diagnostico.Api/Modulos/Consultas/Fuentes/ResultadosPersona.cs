namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Resultado normalizado del RUES (HU #13792, F05-C9, decisión Q2): si la matrícula mercantil apareció, su estado y la última
/// renovación (AAAA-MM-DD, o el año si el proveedor solo informa el último año renovado). Nunca la razón social, el NIT, los
/// representantes ni la dirección.
/// </summary>
public sealed record ResultadoRues(bool Encontrada, string? EstadoMatricula, string? UltimaRenovacion);

/// <summary>Una licencia de conducción normalizada: categoría, estado y vigencia (AAAA-MM-DD si el proveedor la informa).</summary>
public sealed record LicenciaConduccion(string Categoria, string? Estado, string? VigenteHasta);

/// <summary>
/// Resultado normalizado del RUNT persona (HU #13792, F05-C4): si la persona está inscrita, el estado de conductor y sus
/// licencias. Sin nombres, sin documento y sin multas (eso es SIMIT, F05-H5).
/// </summary>
public sealed record ResultadoPersonaRunt(bool Inscrito, string? EstadoConductor, IReadOnlyList<LicenciaConduccion> Licencias);
