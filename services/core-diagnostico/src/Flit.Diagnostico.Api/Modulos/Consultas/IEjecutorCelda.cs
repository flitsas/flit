using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecuta la celda de UNA fuente (HU #13727): el procesador genérico de la cola la toma, fija la compañía de la celda y le
/// entrega aquí el contexto. La lógica de cada fuente (RUNT en #13730, las demás después) vive en su ejecutor; el procesador
/// solo toma celdas de las fuentes que tienen uno registrado. El ejecutor lee y escribe SOLO con <see cref="ContextoCelda.Db"/>,
/// que ya filtra y valida con la empresa de la celda, nunca con un <c>DiagnosticoDb</c> inyectado (ese no tiene empresa).
/// </summary>
public interface IEjecutorCelda
{
    /// <summary>Fuente que atiende.</summary>
    Fuente Fuente { get; }

    /// <summary>Consulta la fuente y devuelve cómo queda la celda; quien la cierra es el procesador.</summary>
    Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct);
}

/// <summary>La celda tomada y la base con la empresa de esa celda.</summary>
public sealed record ContextoCelda(CeldaTomada Celda, DiagnosticoDb Db);

/// <summary>Cómo queda una celda tras ejecutarla: completada con su dato, en error o N/A con motivo.</summary>
public sealed record ResultadoCelda(EstadoCelda Estado, string? Payload, MotivoNa? MotivoNa, OrigenCelda? Origen, DateTimeOffset? DatoObtenidoEn)
{
    /// <summary>Con dato de la fuente (objeto JSON normalizado, R-13).</summary>
    public static ResultadoCelda Completado(string payload, DateTimeOffset obtenidoEn) => new(EstadoCelda.Completado, payload, null, OrigenCelda.Api, obtenidoEn);

    /// <summary>Falló; se puede reprocesar.</summary>
    public static ResultadoCelda Error() => new(EstadoCelda.Error, null, null, null, null);

    /// <summary>No aplica, con su motivo.</summary>
    public static ResultadoCelda Na(MotivoNa motivo) => new(EstadoCelda.Na, null, motivo, null, null);
}
