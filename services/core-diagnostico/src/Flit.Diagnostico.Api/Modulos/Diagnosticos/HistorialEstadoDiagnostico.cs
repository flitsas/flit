using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Una transición de estado del lote (<c>diagnostico.historial_estado_diagnostico</c>, HU #13738, plan §5, R-8): de qué
/// estado a cuál, qué la disparó, quién y cuándo. Solo se inserta: no se edita ni se borra.
/// </summary>
public sealed class HistorialEstadoDiagnostico : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del lote; la misma de la cabecera (lo exige la FK compuesta).</summary>
    public Guid TenantId { get; set; }

    public Guid DiagnosticoId { get; init; }

    /// <summary>Estado de partida; null en la creación del lote.</summary>
    public EstadoDiagnostico? EstadoAnterior { get; init; }

    public EstadoDiagnostico EstadoNuevo { get; init; }

    public DisparadorEstado Disparador { get; init; }

    /// <summary>Usuario que lo provocó; null cuando lo dispara el procesador.</summary>
    public Guid? UsuarioId { get; init; }

    /// <summary>Momento de la transición.</summary>
    public DateTimeOffset En { get; init; }
}
