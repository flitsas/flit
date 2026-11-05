using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// <c>data</c> del evento <c>diagnostico.dictamen.emitido</c> v1 (<c>contracts/asyncapi/diagnostico-events.v1.yaml</c>).
/// Sale cuando nace una versión: el cierre y el override. No lleva placa, VIN, documentos ni el texto de los alertamientos.
/// La empresa va en el sobre. Los consumidores deduplican por <c>(vehiculoId, version)</c>.
/// </summary>
public sealed record DictamenEmitido(
    Guid DiagnosticoId,
    Guid VehiculoId,
    Guid DictamenId,
    int Version,
    string Veredicto,
    int CatalogoVersion,
    string Modo,
    string TipoTramiteCodigo,
    string EstadoRedaccion,
    [property: JsonPropertyName("override")] bool EsOverride)
{
    /// <summary>Tipo (y llave de ruteo) del evento en <c>flit.diagnostico</c>.</summary>
    public const string Tipo = "diagnostico.dictamen.emitido";

    /// <summary>Versión del sobre de este evento.</summary>
    public const int VersionContrato = 1;

    /// <summary>Arma el <c>data</c> de la versión que acaba de nacer. <paramref name="esOverride"/> es true solo en el reemplazo.</summary>
    public static DictamenEmitido De(CabeceraDiagnostico cabecera, DictamenVehiculo fila, bool esOverride)
    {
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(fila);
        return new DictamenEmitido(
            cabecera.Id,
            fila.VehiculoId,
            fila.Id,
            fila.Version,
            fila.Veredicto,
            fila.CatalogoVersion,
            CodigosDeEnum.Codigo(cabecera.Modo),
            cabecera.TipoTramiteCodigo ?? string.Empty,
            fila.EstadoRedaccion,
            esOverride);
    }

    /// <summary>
    /// Encola el evento en la outbox del mismo contexto, sin guardar: lo guarda quien abrió la transacción. Si esa
    /// transacción revierte, el evento también.
    /// </summary>
    internal static DictamenEmitido Encolar(IPlatformOutbox outbox, CabeceraDiagnostico cabecera, DictamenVehiculo fila, bool esOverride)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        var evento = De(cabecera, fila, esOverride);
        outbox.Enqueue(Tipo, VersionContrato, cabecera.TenantId, evento);
        return evento;
    }
}
