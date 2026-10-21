using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Cabecera de un diagnóstico (<c>diagnostico.diagnosticos</c>, HU #13738, plan §5, MOD01): el lote con su nombre, modo,
/// alcance y la copia de la configuración de la compañía al guardarlo. Modo, tipo de trámite, alcance, configuración,
/// creador y fecha de creación no cambian después de guardar. Se llama «cabecera» porque <c>Diagnostico</c> choca con el
/// espacio de nombres <c>Flit.Diagnostico</c>.
/// </summary>
public sealed class CabeceraDiagnostico : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del lote.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Nombre del lote dado por el usuario.</summary>
    public required string Nombre { get; set; }

    /// <summary>Plan comercial; Con Análisis IA exige <see cref="TipoTramiteCodigo"/>.</summary>
    public ModoDiagnostico Modo { get; set; }

    /// <summary>Código del tipo de trámite (<c>tramites.procedure_types</c>); obligatorio en Con Análisis IA.</summary>
    public string? TipoTramiteCodigo { get; set; }

    /// <summary>Creado por la captura express (un vehículo) en lugar de por plantilla.</summary>
    public bool EsExpress { get; set; }

    /// <summary>Fuentes a consultar; RUNT siempre está.</summary>
    public List<Fuente> Alcance { get; set; } = [Fuente.Runt];

    /// <summary>Copia en JSON (objeto) de la configuración de la compañía al guardar.</summary>
    public required string Configuracion { get; set; }

    /// <summary>Estimado de consultas por fuente calculado al confirmar (objeto JSON, R-11); null hasta confirmar.</summary>
    public string? EstimadoConsultas { get; set; }

    public EstadoDiagnostico Estado { get; set; } = EstadoDiagnostico.Creado;

    /// <summary>Sube con cada cambio de celda; el tablero lo usa para el polling (<c>?desde=</c>).</summary>
    public long VersionTablero { get; set; }

    /// <summary>Usuario que creó el lote (claim <c>sub</c>).</summary>
    public Guid UsuarioCreadorId { get; set; }

    /// <summary>Momento de creación (instante, con zona).</summary>
    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>Momento en que el lote terminó (completo o incompleto); null mientras sigue abierto.</summary>
    public DateTimeOffset? CerradoEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): el procesador y el usuario no se pisan.</summary>
    public uint Version { get; set; }
}
