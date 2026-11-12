using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Tramites.Grpc.V1;
using Google.Protobuf.WellKnownTypes;
using DocumentoRpc = Flit.Tramites.Grpc.V1.DocumentoPersona;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Arma la solicitud del rpc <c>CrearBorradorDesdeDiagnostico</c> desde el vehículo (Punto 4, diseño §5.4):
/// <list type="bullet">
/// <item><c>external_ref</c> = id del vehículo, <c>diagnostico_id</c> = su lote, <c>intento_id</c> = id del ítem, <c>usuario_id</c> =
/// quien lanzó la acción (autor del borrador, 4.6), <c>tipo_tramite_codigo</c> ya resuelto.</item>
/// <item><c>placa</c> y <c>vin</c> del vehículo; OT de matrícula: <c>organismo_nombre</c> del RUNT tal cual y el código de la réplica
/// si el nombre casó con uno solo (lo resuelve el llamador).</item>
/// <item>Propietario (<c>propietario_doc_*</c>) y locatario vigente (<c>locatario_doc_*</c>): tipo y número, descifrado SOLO aquí
/// (ADR-0076); sin tipo o sin documento, no viaja.</item>
/// <item><c>vehiculo</c>: clase, servicio (o la columna <c>servicio</c>), marca, línea, modelo, combustible y cilindraje de
/// <c>datos_consolidados.vehiculo</c> como texto; el color no lo guarda Diagnóstico y va vacío. La hidratación RUNT de Trámites
/// prevalece.</item>
/// <item><c>runt_consultado_en</c> si hay.</item>
/// </list>
/// Nada de esto se escribe en logs. El nombre del propietario no se guarda en Diagnóstico y no viaja.
/// </summary>
public static class MapeoBorradorTramite
{
    public static CrearBorradorDesdeDiagnosticoRequest Armar(
        VehiculoDiagnostico vehiculo, string tipoTramite, Guid usuario, Guid item, string? organismoCodigo, IProtectorDocumentoPersona protector)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoTramite);
        ArgumentNullException.ThrowIfNull(protector);
        var datos = DatosVehiculo(vehiculo.DatosConsolidados);
        return new CrearBorradorDesdeDiagnosticoRequest
        {
            ExternalRef = vehiculo.Id.ToString(),
            UsuarioId = usuario.ToString(),
            TipoTramiteCodigo = tipoTramite,
            Placa = Texto(vehiculo.Placa),
            Vin = Texto(vehiculo.Vin),
            OrganismoNombreRunt = Texto(vehiculo.OrganismoNombre),
            OrganismoCodigo = Texto(organismoCodigo),
            Propietario = Documento(vehiculo.PropietarioDocTipo, vehiculo.PropietarioDocCifrado, protector),
            Locatario = Documento(vehiculo.LocatarioDocTipo, vehiculo.LocatarioDocCifrado, protector),
            Vehiculo = new VehiculoBorrador
            {
                Clase = Campo(datos, "clase"),
                Servicio = Campo(datos, "servicio") is { Length: > 0 } servicio ? servicio : Texto(vehiculo.Servicio),
                Marca = Campo(datos, "marca"),
                Linea = Campo(datos, "linea"),
                Modelo = Campo(datos, "modelo"),
                Combustible = Campo(datos, "combustible"),
                Cilindraje = Campo(datos, "cilindraje"),
                Color = Campo(datos, "color"),
            },
            DiagnosticoId = vehiculo.DiagnosticoId.ToString(),
            RuntConsultadoEn = vehiculo.RuntConsultadoEn is { } runt ? Timestamp.FromDateTimeOffset(runt) : null,
            IntentoId = item.ToString(),
        };
    }

    private static DocumentoRpc? Documento(string? tipo, byte[]? cifrado, IProtectorDocumentoPersona protector) =>
        string.IsNullOrWhiteSpace(tipo) || cifrado is not { Length: > 0 }
            ? null
            : new DocumentoRpc { TipoDocumento = tipo.Trim(), NumeroDocumento = protector.Desproteger(cifrado) };

    /// <summary>El objeto <c>vehiculo</c> de <c>datos_consolidados</c> (lo escribe la celda RUNT); null si no hay o no es objeto.</summary>
    private static JsonElement? DatosVehiculo(string? consolidados)
    {
        if (string.IsNullOrWhiteSpace(consolidados))
            return null;
        try
        {
            using var documento = JsonDocument.Parse(consolidados);
            return documento.RootElement.ValueKind == JsonValueKind.Object
                && documento.RootElement.TryGetProperty("vehiculo", out var v) && v.ValueKind == JsonValueKind.Object
                    ? v.Clone()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Texto del campo (los números del RUNT, como el modelo, también); vacío si no está o es null.</summary>
    private static string Campo(JsonElement? datos, string nombre) =>
        datos is { } d && d.TryGetProperty(nombre, out var valor)
            ? valor.ValueKind switch
            {
                JsonValueKind.String => Texto(valor.GetString()),
                JsonValueKind.Number => valor.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;

    private static string Texto(string? valor) => valor?.Trim() ?? string.Empty;
}
