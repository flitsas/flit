using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Configuracion;

/// <summary>
/// <c>GET/PUT /configuracion</c> (HU #13741, plan §6.1): los dos niveles de la compañía del token. El contractual va marcado
/// de solo lectura. Nunca trae <c>redaccion_ia_suspendida</c> ni el autor de los cambios contractuales (un usuario del
/// backoffice FLIT). Los valores son los códigos de la base (<c>RUNT</c>, <c>EMPRESA</c>, <c>NO_MOSTRAR</c>).
/// </summary>
public sealed record ConfiguracionDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("contrato")] NivelContractualDto Contrato,
    [property: JsonPropertyName("operacion")] NivelOperativoDto Operacion)
{
    public static ConfiguracionDto De(ConfiguracionEmpresa c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(
            c.TenantId,
            new NivelContractualDto(true, Codigos.De(c.FuentesHabilitadas), c.LimiteVehiculosLote, c.ContratoActualizadoEn),
            new NivelOperativoDto(
                CodigosDeEnum.Codigo(c.VisibilidadUsuario), Codigos.De(c.FuentesPorDefecto), CodigosDeEnum.Codigo(c.EstimadoConsultas),
                c.OperativaActualizadaPor, c.OperativaActualizadaEn));
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
/// backoffice recorta al reducir las habilitadas).
/// </summary>
public sealed record ContratoBackofficeDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("fuentes_habilitadas")] IReadOnlyList<string> FuentesHabilitadas,
    [property: JsonPropertyName("limite_vehiculos_lote")] int LimiteVehiculosLote,
    [property: JsonPropertyName("redaccion_ia_suspendida")] bool RedaccionIaSuspendida,
    [property: JsonPropertyName("fuentes_por_defecto")] IReadOnlyList<string> FuentesPorDefecto,
    [property: JsonPropertyName("actualizado_por")] Guid? ActualizadoPor,
    [property: JsonPropertyName("actualizado_en")] DateTimeOffset? ActualizadoEn)
{
    public static ContratoBackofficeDto De(ConfiguracionEmpresa c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(
            c.TenantId, Codigos.De(c.FuentesHabilitadas), c.LimiteVehiculosLote, c.RedaccionIaSuspendida, Codigos.De(c.FuentesPorDefecto),
            c.ContratoActualizadoPor, c.ContratoActualizadoEn);
    }
}

/// <summary>Códigos de la base de una lista de fuentes.</summary>
internal static class Codigos
{
    public static IReadOnlyList<string> De(IEnumerable<Fuente> fuentes) => fuentes.Select(CodigosDeEnum.Codigo).ToList();
}
