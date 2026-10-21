using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Un intento de consulta a una fuente (<c>diagnostico.intentos_consulta</c>, HU #13723, plan §5, R-1): auditoría y log de
/// negocio de cada llamada, del reproceso y del llenado manual. Solo se inserta: no se edita ni se borra (ADR-0072, clase b).
/// Desde HU #13737 el procesador inserta uno por cada ejecución de una celda (<see cref="RegistroIntentos"/>).
/// </summary>
public sealed class IntentoConsulta : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña de la celda (lo exige la FK compuesta).</summary>
    public Guid TenantId { get; set; }

    public Guid DiagnosticoId { get; init; }

    public Guid VehiculoId { get; init; }

    /// <summary>Celda a la que pertenece el intento.</summary>
    public Guid ConsultaId { get; init; }

    public Fuente Fuente { get; init; }

    /// <summary>
    /// Identificador consultado (placa o VIN, HU #13737); null si la fuente se consulta por persona: nunca un documento en claro.
    /// </summary>
    public string? IdentificadorConsultado { get; init; }

    /// <summary>Proveedor que respondió; null si no llegó a ninguno o fue manual.</summary>
    public ServicioConsulta? Servicio { get; init; }

    public DisparadorIntento Disparador { get; init; }

    public DateTimeOffset Inicio { get; init; }

    public DateTimeOffset Fin { get; init; }

    public ResultadoIntento Estado { get; init; }

    /// <summary>Tipo de error de la fuente (patrón Comparendos); null si fue exitoso.</summary>
    public string? TipoError { get; init; }

    public string? MensajeError { get; init; }

    /// <summary>Transacción del proveedor; null en el manual (12659 #6).</summary>
    public string? TransaccionProveedorId { get; init; }

    /// <summary>El dato salió de la reutilización del mismo día (D-10), sin consumo.</summary>
    public bool DesdeCache { get; init; }

    /// <summary>Usuario del reproceso o del manual; null cuando lo dispara el procesador.</summary>
    public Guid? UsuarioId { get; init; }
}
