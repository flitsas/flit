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

    /// <summary>Datos del vehículo consolidados de las fuentes (objeto JSON), sin documentos en claro.</summary>
    public string DatosConsolidados { get; set; } = "{}";

    /// <summary>Instante de la última consulta al RUNT; Trámites decide con él si lo reutiliza (R-14).</summary>
    public DateTimeOffset? RuntConsultadoEn { get; set; }

    /// <summary>Estado derivado de las celdas del vehículo.</summary>
    public string EstadoConsultas { get; set; } = "PENDIENTE";

    /// <summary>Posición del vehículo dentro del lote.</summary>
    public int Orden { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>): el procesador y el usuario no se pisan.</summary>
    public uint Version { get; set; }
}
