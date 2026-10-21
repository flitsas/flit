using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// Valor inmutable de la configuración vigente de la compañía (HU #13741, escenario 13). F01 la toma al crear un diagnóstico
/// y guarda <see cref="ComoJson"/> en la cabecera (<c>diagnosticos.configuracion</c>, objeto): editar después la
/// configuración no cambia la copia. No incluye <c>redaccion_ia_suspendida</c>: el interruptor de emergencia (R-2) actúa en
/// el momento y no se congela. La verificación sobre diagnósticos ya creados se cierra en F01-C8.
/// </summary>
public sealed record CopiaConfiguracion
{
    private CopiaConfiguracion(
        IReadOnlyList<Fuente> fuentesHabilitadas, int limiteVehiculosLote, VisibilidadUsuario visibilidadUsuario,
        IReadOnlyList<Fuente> fuentesPorDefecto, EstimadoConsultas estimadoConsultas)
    {
        FuentesHabilitadas = fuentesHabilitadas;
        LimiteVehiculosLote = limiteVehiculosLote;
        VisibilidadUsuario = visibilidadUsuario;
        FuentesPorDefecto = fuentesPorDefecto;
        EstimadoConsultas = estimadoConsultas;
    }

    public IReadOnlyList<Fuente> FuentesHabilitadas { get; }

    public int LimiteVehiculosLote { get; }

    public VisibilidadUsuario VisibilidadUsuario { get; }

    public IReadOnlyList<Fuente> FuentesPorDefecto { get; }

    public EstimadoConsultas EstimadoConsultas { get; }

    /// <summary>Copia de los valores de la entidad (las listas se copian: la copia no comparte nada con ella).</summary>
    public static CopiaConfiguracion De(ConfiguracionEmpresa configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        return new(
            Array.AsReadOnly(configuracion.FuentesHabilitadas.ToArray()), configuracion.LimiteVehiculosLote, configuracion.VisibilidadUsuario,
            Array.AsReadOnly(configuracion.FuentesPorDefecto.ToArray()), configuracion.EstimadoConsultas);
    }

    /// <summary>Copia de la configuración de la compañía del contexto; sin fila guardada, la de los valores por defecto.</summary>
    public static async Task<CopiaConfiguracion> TomarAsync(DiagnosticoDb db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var configuracion = await db.Set<ConfiguracionEmpresa>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return De(configuracion ?? new ConfiguracionEmpresa());
    }

    /// <summary>Objeto JSON con los códigos de la base, para la columna <c>diagnosticos.configuracion</c>.</summary>
    public string ComoJson() => JsonSerializer.Serialize(new Serializada(
        Codigos.De(FuentesHabilitadas), LimiteVehiculosLote, CodigosDeEnum.Codigo(VisibilidadUsuario), Codigos.De(FuentesPorDefecto),
        CodigosDeEnum.Codigo(EstimadoConsultas)));

    private sealed record Serializada(
        [property: JsonPropertyName("fuentes_habilitadas")] IReadOnlyList<string> FuentesHabilitadas,
        [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
        [property: JsonPropertyName("visibilidad_usuario")] string VisibilidadUsuario,
        [property: JsonPropertyName("fuentes_por_defecto")] IReadOnlyList<string> FuentesPorDefecto,
        [property: JsonPropertyName("estimado_consultas")] string EstimadoConsultas);
}
