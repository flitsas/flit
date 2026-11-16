using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Vehiculos;

/// <summary>
/// Vehículo de un lote (<c>diagnostico.vehiculos</c>, HU #13735, plan §5, MOD02): el identificador tal como llegó y
/// normalizado, su clase, los datos del RUNT y los documentos de propietario y locatario cifrados. El identificador
/// normalizado no se repite dentro del lote. Cuelga de la cabecera con la FK compuesta por empresa.
/// </summary>
public sealed class VehiculoDiagnostico : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa dueña del lote; la misma de la cabecera (lo exige la FK compuesta).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Cabecera del lote.</summary>
    public Guid DiagnosticoId { get; init; }

    /// <summary>Identificador tal como lo escribió el cliente.</summary>
    public required string IdentificadorOriginal { get; set; }

    /// <summary>Identificador en mayúsculas y sin espacios; único dentro del lote.</summary>
    public required string IdentificadorNorm { get; set; }

    public TipoIdentificador TipoIdentificador { get; set; }

    public string? Placa { get; set; }

    public string? Vin { get; set; }

    /// <summary>Servicio del vehículo normalizado (<c>vehicle_service</c> del RUNT).</summary>
    public string? Servicio { get; set; }

    /// <summary>Nombre del organismo de tránsito (<c>transit_office_name</c> del RUNT).</summary>
    public string? OrganismoNombre { get; set; }

    /// <summary>Departamento derivado de la réplica de organismos.</summary>
    public string? DepartamentoCodigo { get; set; }

    /// <summary>Tipo de documento del propietario, hidratado del RUNT por placa (D-02).</summary>
    public string? PropietarioDocTipo { get; set; }

    /// <summary>Documento del propietario cifrado (plan §9.3). El nombre no se guarda.</summary>
    public byte[]? PropietarioDocCifrado { get; set; }

    /// <summary>Tipo de documento del locatario.</summary>
    public string? LocatarioDocTipo { get; set; }

    /// <summary>Documento del locatario cifrado; exige <see cref="LocatarioDocOrigen"/>.</summary>
    public byte[]? LocatarioDocCifrado { get; set; }

    /// <summary>Origen del documento del locatario; null si no hay documento.</summary>
    public OrigenDocumento? LocatarioDocOrigen { get; set; }

    /// <summary>
    /// Tipo del documento del propietario actual que declaró el cliente (opcional, Express o carga masiva; HU #14582). Solo sirve para
    /// compararlo con el propietario del RUNT: no alimenta ninguna consulta. Va siempre junto con <see cref="PropietarioDeclaradoDocCifrado"/>.
    /// </summary>
    public string? PropietarioDeclaradoDocTipo { get; set; }

    /// <summary>Documento del propietario actual declarado, cifrado con <c>IProtectorDocumentoPersona</c>; nunca en claro ni en logs.</summary>
    public byte[]? PropietarioDeclaradoDocCifrado { get; set; }

    /// <summary>
    /// Resultado de <c>ComparacionPropietario</c> (HU #14582, SP-1): true si el declarado no coincide con ningún propietario del RUNT,
    /// false si coincide, null sin declarado o sin RUNT cerrado. Se calcula una vez, al cerrar la celda RUNT (también la reutilizada),
    /// y las lecturas solo leen la columna.
    /// </summary>
    public bool? CambioPropietario { get; set; }

    /// <summary>Datos del vehículo consolidados de las fuentes (objeto JSON), sin documentos en claro.</summary>
    public string DatosConsolidados { get; set; } = "{}";

    /// <summary>Instante de la última consulta al RUNT; Trámites decide con él si lo reutiliza (R-14).</summary>
    public DateTimeOffset? RuntConsultadoEn { get; set; }

    /// <summary>Estado derivado de las celdas del vehículo.</summary>
    public string EstadoConsultas { get; set; } = "PENDIENTE";

    /// <summary>Posición del vehículo dentro del lote.</summary>
    public int Orden { get; set; }

    /// <summary>
    /// Borrador creado en Trámites desde este vehículo (Punto 4, ADR-0084: <c>origin=diagnostico</c>, <c>external_ref</c> = id del
    /// vehículo); null mientras no haya. Se fija una sola vez (R-16) y es la fuente de «Ver trámite #N» y de la elegibilidad.
    /// </summary>
    public Guid? TramiteId { get; set; }

    /// <summary>Número del trámite (<c>reference_number</c> de Trámites); lo lee también el índice del ZIP (Punto 8).</summary>
    public string? TramiteNumero { get; set; }

    /// <summary>Instante en que Diagnóstico registró el trámite.</summary>
    public DateTimeOffset? TramiteCreadoEn { get; set; }

    /// <summary>
    /// Versión de fila del tablero (HU #14563, ADR-0086 D-1): el valor de <c>diagnosticos.version_tablero</c> en el último cambio
    /// de la fila (dictamen nuevo o ajustado, trámite creado, omitido o fallido). 0 = sin cambios de fila. Nunca supera la versión
    /// del lote. Se fija con <c>EscritorCeldas.SubirVersionFilaAsync</c> o <c>EscritorCeldas.MarcarFila</c>.
    /// </summary>
    public long VersionTablero { get; set; }

    /// <summary>
    /// Estado del saneamiento no anulado del vehículo (HU #14576, ADR-0087), copia para las grillas; null si no hay. Lo escribe solo
    /// <c>EscritorSaneamiento</c>, con <c>ExecuteUpdate</c> junto con <see cref="VersionTablero"/> y sin cargar la fila (C19).
    /// </summary>
    public Flit.Diagnostico.Domain.Saneamiento.EstadoSaneamiento? SaneamientoEstado { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): el procesador y el usuario no se pisan.</summary>
    public uint Version { get; set; }
}
