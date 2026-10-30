using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;
using Grpc.Core;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Armadores de <see cref="ResultadoConsulta"/> para las pruebas de las consultas del portal (Feature #14022). Todos los
/// datos son ficticios: placas, documentos, nombres y acreedores inventados, sin relación con personas ni vehículos reales.
/// </summary>
internal static class ResultadosFalsos
{
    /// <summary>Vehículo con TODO lo que el RUNT devolvería, incluidos los datos que el portal no puede exponer.</summary>
    public static ResultadoConsulta VehiculoCompleto(bool prenda = false, bool limitacion = false)
    {
        var r = Nuevo(Semaforo.Verde);
        r.Chequeos.Add(Chequeo("estado_vehiculo", EstadoChequeo.Ok));
        Texto(r, "plate", "ABC123");
        Texto(r, "vin", "9BWZZZ377VT004251");
        Texto(r, "vehicle_class", "AUTOMOVIL");
        Texto(r, "vehicle_brand", "MARCA-FICTICIA");
        Texto(r, "vehicle_line", "LINEA-FICTICIA");
        Texto(r, "vehicle_year", "2018");
        Texto(r, "vehicle_service", "PARTICULAR");
        Texto(r, "vehicle_state", "activo");
        Texto(r, "transit_office_name", "ORGANISMO FICTICIO");
        Texto(r, "vehicle_color", "ROJO");
        Texto(r, "vehicle_fuel", "GASOLINA");
        Texto(r, "vehicle_engine_displacement", "1600");
        Texto(r, "vehicle_engine_number", "MOTOR-FICTICIO-1");
        Texto(r, "vehicle_series", "SERIE-FICTICIA-1");
        // Datos de terceros que el RUNT trae y que jamás deben salir de core-personanatural.
        Texto(r, "owner_document_type", "CC");
        Texto(r, "owner_document_number", "1098765432");
        Texto(r, "person_full_name", "PROPIETARIO FICTICIO PEREZ");
        Texto(r, "owner_address", "CALLE FICTICIA 1 # 2-3");
        Texto(r, "owner_phone", "3000000000");
        Texto(r, "soat_vencimiento", "2027-01-01");
        Texto(r, "runt_tiene_prendas", prenda ? "SI" : "NO");
        Texto(r, "runt_tiene_gravamenes", "NO");
        Texto(r, "runt_nombre_acreedor", prenda ? "BANCO FICTICIO S.A." : null);
        r.Campos.Add(new Campo { Clave = "runt_gravamenes", ValorJson = prenda ? "[{\"nombreAcreedor\":\"BANCO FICTICIO S.A.\"}]" : "[]" });
        r.Campos.Add(new Campo { Clave = "runt_limitaciones_propiedad", ValorJson = limitacion ? "[{\"aFavorDe\":\"PERSONA FICTICIA\"}]" : "[]" });
        return r;
    }

    /// <summary>Lo que core-consultas devuelve cuando el RUNT dice que el vehículo no existe: un chequeo FAIL y ningún campo.</summary>
    public static ResultadoConsulta VehiculoInexistente()
    {
        var r = Nuevo(Semaforo.Rojo);
        r.Chequeos.Add(Chequeo("vehiculo", EstadoChequeo.Fail, "Vehículo no encontrado en RUNT"));
        return r;
    }

    /// <summary>Proveedor caído: NO es un error gRPC, llega como un chequeo en ERROR.</summary>
    public static ResultadoConsulta ProveedorCaido()
    {
        var r = Nuevo(Semaforo.Rojo);
        r.Chequeos.Add(Chequeo("provider", EstadoChequeo.Error, "No fue posible verificar la información"));
        return r;
    }

    public static ResultadoConsulta Conductor(bool licenciaActiva = true, string? estadoConductor = "ACTIVO", bool conMultas = true)
    {
        var r = Nuevo(Semaforo.Verde);
        r.Chequeos.Add(Chequeo("conductor_identidad", EstadoChequeo.Ok));
        Texto(r, "person_first_name", "NOMBRE-FICTICIO");
        Texto(r, "person_last_name", "APELLIDO-FICTICIO");
        Texto(r, "person_full_name", "NOMBRE-FICTICIO APELLIDO-FICTICIO");
        Texto(r, "person_birth_date", "1990-05-17");
        Texto(r, "person_license_status", estadoConductor);
        Texto(r, "person_citizen_status", "ACTIVA");
        if (conMultas)
        {
            Texto(r, "person_has_pending_fines", "true");
            Texto(r, "person_paz_y_salvo", "PYS-FICTICIO-9");
        }

        Texto(r, "person_has_active_license", licenciaActiva ? "true" : "false");
        if (licenciaActiva)
            Texto(r, "person_license_categories", "B1,C1");
        return r;
    }

    /// <summary>Conductor con el nombre desglosado como lo hidrata core-consultas (dos nombres y dos apellidos).</summary>
    public static ResultadoConsulta ConductorConNombres()
    {
        var r = Conductor();
        r.Campos.Clear();
        Texto(r, "person_full_name", "ANA MARIA GOMEZ RUIZ");
        Texto(r, "person_first_name", "ANA");
        Texto(r, "person_second_name", "MARIA");
        Texto(r, "person_first_last_name", "GOMEZ");
        Texto(r, "person_second_last_name", "RUIZ");
        Texto(r, "person_last_name", "GOMEZ RUIZ");
        Texto(r, "person_birth_date", "1990-05-17");
        Texto(r, "person_license_status", "ACTIVO");
        Texto(r, "person_has_pending_fines", "true");
        Texto(r, "person_paz_y_salvo", "PYS-FICTICIO-9");
        return r;
    }

    /// <summary>Persona que el RUNT no halla: chequeo UNKNOWN y ningún campo.</summary>
    public static ResultadoConsulta ConductorInexistente()
    {
        var r = Nuevo(Semaforo.Amarillo);
        r.Chequeos.Add(Chequeo("conductor", EstadoChequeo.Unknown, "Persona no encontrada en RUNT"));
        return r;
    }

    private static ResultadoConsulta Nuevo(Semaforo semaforo) => new() { Proveedor = "kyverum_runt", Semaforo = semaforo };

    private static Chequeo Chequeo(string clave, EstadoChequeo estado, string? mensaje = null)
    {
        var c = new Chequeo { Clave = clave, Etiqueta = clave, Estado = estado, Fuente = "RUNT" };
        if (mensaje is not null)
            c.Mensaje = mensaje;
        return c;
    }

    private static void Texto(ResultadoConsulta r, string clave, string? valor)
    {
        var campo = new Campo { Clave = clave };
        if (valor is not null)
            campo.ValorTexto = valor;
        r.Campos.Add(campo);
    }
}

/// <summary>Proveedor falso: responde lo que la prueba le dé y deja a la vista lo que el portal le pidió.</summary>
internal sealed class ConsultasRuntFalso : IConsultasRunt
{
    public Func<ResultadoConsulta> Vehiculo { get; set; } = ResultadosFalsos.VehiculoInexistente;

    public Func<ResultadoConsulta> Conductor { get; set; } = ResultadosFalsos.ConductorInexistente;

    public Exception? Falla { get; set; }

    public List<(string? Placa, string? Vin, DocumentoConsulta? Propietario, string? Referencia)> LlamadasVehiculo { get; } = [];

    public List<(DocumentoConsulta Documento, string? Referencia)> LlamadasConductor { get; } = [];

    public Task<ResultadoConsulta> ConsultarVehiculoAsync(string? placa, string? vin, DocumentoConsulta? propietario, string? referencia, CancellationToken ct)
    {
        LlamadasVehiculo.Add((placa, vin, propietario, referencia));
        return Falla is null ? Task.FromResult(Vehiculo()) : Task.FromException<ResultadoConsulta>(Falla);
    }

    public Task<ResultadoConsulta> ConsultarConductorAsync(DocumentoConsulta documento, string? referencia, CancellationToken ct)
    {
        LlamadasConductor.Add((documento, referencia));
        return Falla is null ? Task.FromResult(Conductor()) : Task.FromException<ResultadoConsulta>(Falla);
    }
}

/// <summary>
/// Cliente gRPC falso de core-consultas (el generado trae un constructor protegido justo para esto): captura lo que el
/// adaptador envía —cabeceras y petición— y responde o falla según la prueba.
/// </summary>
internal sealed class ClienteConsultasFalso : ConsultasService.ConsultasServiceClient
{
    public Func<ResultadoConsulta>? Responder { get; set; }

    public RpcException? Falla { get; set; }

    public ConsultarVehiculoRequest? UltimaPeticionVehiculo { get; private set; }

    public ConsultarConductorRequest? UltimaPeticionConductor { get; private set; }

    public Metadata? UltimasCabeceras { get; private set; }

    public override AsyncUnaryCall<ConsultarVehiculoResponse> ConsultarVehiculoAsync(ConsultarVehiculoRequest request, Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        UltimaPeticionVehiculo = request;
        UltimasCabeceras = headers;
        return Llamada(() => new ConsultarVehiculoResponse { Resultado = Responder!() });
    }

    public override AsyncUnaryCall<ConsultarConductorResponse> ConsultarConductorAsync(ConsultarConductorRequest request, Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        UltimaPeticionConductor = request;
        UltimasCabeceras = headers;
        return Llamada(() => new ConsultarConductorResponse { Resultado = Responder!() });
    }

    private AsyncUnaryCall<T> Llamada<T>(Func<T> respuesta) =>
        new(Falla is null ? Task.FromResult(respuesta()) : Task.FromException<T>(Falla),
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
}
