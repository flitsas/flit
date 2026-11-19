using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// <c>GET/PUT /configuracion</c> (HU #13741, plan §6.1): los dos niveles de la compañía del token. El contractual va marcado
/// de solo lectura. Nunca trae <c>redaccion_ia_suspendida</c> ni el autor de los cambios contractuales (un usuario del
/// backoffice FLIT). Los valores son los códigos de la base (<c>RUNT</c>, <c>EMPRESA</c>, <c>NO_MOSTRAR</c>).
/// <c>fuentes_disponibles</c> (HU #13863) son las que hoy se pueden habilitar y pedir, en el orden del enum, con la misma regla
/// del 422 <c>FUENTE_NO_DISPONIBLE</c> (<see cref="Consultas.FuentesDisponibles.Todas"/>).
/// </summary>
public sealed record ConfiguracionDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("contrato")] NivelContractualDto Contrato,
    [property: JsonPropertyName("operacion")] NivelOperativoDto Operacion,
    [property: JsonPropertyName("fuentes_disponibles")] IReadOnlyList<string> FuentesDisponibles)
{
    public static ConfiguracionDto De(ConfiguracionEmpresa c, IEnumerable<Fuente> disponibles)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(disponibles);
        return new(
            c.TenantId,
            new NivelContractualDto(true, Codigos.De(c.FuentesHabilitadas), c.LimiteVehiculosLote, c.ContratoActualizadoEn),
            new NivelOperativoDto(
                CodigosDeEnum.Codigo(c.VisibilidadUsuario), Codigos.De(c.FuentesPorDefecto), CodigosDeEnum.Codigo(c.EstimadoConsultas),
                c.OperativaActualizadaPor, c.OperativaActualizadaEn),
            Codigos.De(disponibles));
    }
}

/// <summary>Nivel contractual visto por la compañía: solo lectura (lo edita el backoffice FLIT).</summary>
public sealed record NivelContractualDto(
    [property: JsonPropertyName("solo_lectura")] bool SoloLectura,
    [property: JsonPropertyName("fuentes_habilitadas")] IReadOnlyList<string> FuentesHabilitadas,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("actualizado_en")] DateTimeOffset? ActualizadoEn);

/// <summary>Nivel operativo: lo edita el admin de la compañía.</summary>
public sealed record NivelOperativoDto(
    [property: JsonPropertyName("visibilidad_usuario")] string VisibilidadUsuario,
    [property: JsonPropertyName("fuentes_por_defecto")] IReadOnlyList<string> FuentesPorDefecto,
    [property: JsonPropertyName("estimado_consultas")] string EstimadoConsultas,
    [property: JsonPropertyName("actualizado_por")] Guid? ActualizadoPor,
    [property: JsonPropertyName("actualizado_en")] DateTimeOffset? ActualizadoEn);

/// <summary>
/// <c>GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato</c> (HU #13741): el nivel contractual de la empresa
/// indicada, el interruptor de emergencia de la redacción IA (R-2) y, en solo lectura, las fuentes por defecto (que el
/// backoffice recorta al reducir las habilitadas). <c>fuentes_disponibles</c> (HU #13863): las que hoy se pueden habilitar, en el
/// orden del enum; una habilitada que dejó de estar disponible sigue en <c>fuentes_habilitadas</c> y no aparece aquí.
/// </summary>
public sealed record ContratoBackofficeDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("fuentes_habilitadas")] IReadOnlyList<string> FuentesHabilitadas,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("redaccion_ia_suspendida")] bool RedaccionIaSuspendida,
    [property: JsonPropertyName("fuentes_por_defecto")] IReadOnlyList<string> FuentesPorDefecto,
    [property: JsonPropertyName("actualizado_por")] Guid? ActualizadoPor,
    [property: JsonPropertyName("actualizado_en")] DateTimeOffset? ActualizadoEn,
    [property: JsonPropertyName("fuentes_disponibles")] IReadOnlyList<string> FuentesDisponibles)
{
    public static ContratoBackofficeDto De(ConfiguracionEmpresa c, IEnumerable<Fuente> disponibles)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(disponibles);
        return new(
            c.TenantId, Codigos.De(c.FuentesHabilitadas), c.LimiteVehiculosLote, c.RedaccionIaSuspendida, Codigos.De(c.FuentesPorDefecto),
            c.ContratoActualizadoPor, c.ContratoActualizadoEn, Codigos.De(disponibles));
    }
}

/// <summary>Códigos de la base de una lista de fuentes.</summary>
internal static class Codigos
{
    public static IReadOnlyList<string> De(IEnumerable<Fuente> fuentes) => fuentes.Select(CodigosDeEnum.Codigo).ToList();
}
