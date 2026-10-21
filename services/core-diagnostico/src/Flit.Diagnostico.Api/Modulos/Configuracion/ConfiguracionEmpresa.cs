using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// Configuración de Diagnóstico de una compañía (<c>diagnostico.configuracion_empresa</c>, HU #13736, plan §5, D-04 = b):
/// una fila por empresa, con el nivel contractual (solo backoffice FLIT) y el operativo (admin de la compañía). La base
/// garantiza que <see cref="FuentesPorDefecto"/> sea subconjunto de <see cref="FuentesHabilitadas"/>. Sin retención ni
/// purga (R-18). Los valores iniciales coinciden con los valores por defecto de la base.
/// </summary>
public sealed class ConfiguracionEmpresa : IEntidadDeEmpresa
{
    /// <summary>Empresa dueña de la configuración; es también la clave primaria.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Contractual: fuentes que la compañía contrató.</summary>
    public List<Fuente> FuentesHabilitadas { get; set; } = [Fuente.Runt];

    /// <summary>Contractual: máximo de vehículos por lote (mayor que cero).</summary>
    public int LimiteVehiculosLote { get; set; } = 500;

    /// <summary>Usuario del backoffice que cambió por última vez el nivel contractual.</summary>
    public Guid? ContratoActualizadoPor { get; set; }

    /// <summary>Momento del último cambio contractual.</summary>
    public DateTimeOffset? ContratoActualizadoEn { get; set; }

    /// <summary>Interruptor de emergencia del backoffice para la redacción IA (R-2). No se muestra al cliente.</summary>
    public bool RedaccionIaSuspendida { get; set; }

    /// <summary>Operativa: qué lotes ve cada usuario.</summary>
    public VisibilidadUsuario VisibilidadUsuario { get; set; } = VisibilidadUsuario.Empresa;

    /// <summary>Operativa: fuentes premarcadas al crear un diagnóstico (subconjunto de las habilitadas).</summary>
    public List<Fuente> FuentesPorDefecto { get; set; } = [Fuente.Runt];

    /// <summary>Operativa: cuándo se muestra el estimado de consultas.</summary>
    public EstimadoConsultas EstimadoConsultas { get; set; } = EstimadoConsultas.NoMostrar;

    /// <summary>Admin de la compañía que cambió por última vez el nivel operativo.</summary>
    public Guid? OperativaActualizadaPor { get; set; }

    /// <summary>Momento del último cambio operativo.</summary>
    public DateTimeOffset? OperativaActualizadaEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): dos ediciones simultáneas no se pisan.</summary>
    public uint Version { get; set; }
}
