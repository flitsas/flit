using System.Reflection;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13873 (F10-H1) — contrato gRPC tipado del RUNT por Intempo (<c>flit.consultas.v1.RuntIntempoService</c>, archivo
/// <c>runt_intempo.proto</c>): dos rpcs, <c>ConsultarPorPlaca</c> y <c>ConsultarPorVin</c>, con la misma respuesta
/// <c>ConsultarVehiculoIntempoResponse</c> (G2=b), y un <c>VehiculoIntempo</c> cuyo proto-JSON tiene las 22 claves del objeto
/// normalizado §5 de la especificación (Q1=b). Las pruebas leen el contrato por reflexión y descriptor: así compilan antes
/// de que el proto exista y un campo renumerado, renombrado o sin <c>json_name</c> las rompe. Datos inventados (TST###,
/// documentos 99999…); nunca muestras reales.
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Consultas.Tests/Flit.Consultas.Tests.csproj --no-build --filter "FullyQualifiedName~ContratoRuntIntempoTests"
/// </code>
/// </example>
public sealed class ContratoRuntIntempoTests
{
    private const string Paquete = "flit.consultas.v1";

    /// <summary>Las 22 claves de §5, en el orden de la especificación.</summary>
    internal static readonly string[] ClavesSeccion5 =
    [
        "plate", "vin", "documentType", "documentNumber", "informacionGeneral", "datosTecnicos", "soat", "tecnoMecanica",
        "garantiasFavorDe", "garantiasMobiliarias", "informacionBlindaje", "limitacionPropiedad", "normalizacionSaneamiento",
        "polizasResponsabilidadCivil", "solicitudes", "tarjetaOperacion", "propietariosActuales", "historicoPropietarios",
        "owner", "lessee", "accidentRate", "infracciones",
    ];

    private static readonly Assembly Contratos = typeof(ConsultasService).Assembly;

    private static FileDescriptor Archivo()
    {
        var reflexion = Contratos.GetType("Flit.Consultas.Grpc.V1.RuntIntempoReflection");
        reflexion.Should().NotBeNull("el contrato debe declarar contracts/proto/flit/consultas/v1/runt_intempo.proto");
        return (FileDescriptor)reflexion!.GetProperty("Descriptor")!.GetValue(null)!;
    }

    private static MessageDescriptor Mensaje(string nombre)
    {
        var m = Archivo().MessageTypes.FirstOrDefault(x => x.Name == nombre);
        m.Should().NotBeNull($"runt_intempo.proto debe declarar el mensaje {nombre}");
        return m!;
    }

    private static void Campo(MessageDescriptor m, string nombre, int numero, FieldType tipo, bool repetido = false,
        string? tipoMensaje = null, bool opcional = false)
    {
        var f = m.FindFieldByName(nombre);
        f.Should().NotBeNull($"{m.Name} debe tener el campo {nombre}");
        f!.FieldNumber.Should().Be(numero, $"{m.Name}.{nombre} es el campo {numero}");
        f.FieldType.Should().Be(tipo, $"{m.Name}.{nombre} es {tipo}");
        f.IsRepeated.Should().Be(repetido, $"{m.Name}.{nombre} repeated={repetido}");
        if (tipoMensaje is not null)
            (tipo == FieldType.Enum ? f.EnumType.FullName : f.MessageType.FullName).Should().Be(tipoMensaje);
        if (opcional)
            f.HasPresence.Should().BeTrue($"{m.Name}.{nombre} es optional");
    }

    [Fact]
    public void El_cliente_y_la_base_generados_exponen_ConsultarPorPlaca_y_ConsultarPorVin_con_la_misma_respuesta()
    {
        var archivo = Archivo();
        archivo.Package.Should().Be(Paquete);
        archivo.Dependencies.Select(d => d.Name).Should().Contain("flit/consultas/v1/fuentes_comparendos.proto",
            "TipoErrorFuente se reutiliza, no se duplica");

        var servicio = archivo.Services.SingleOrDefault(s => s.Name == "RuntIntempoService");
        servicio.Should().NotBeNull("G1=a: servicio propio RuntIntempoService");
        servicio!.Methods.Select(x => x.Name).Should().BeEquivalentTo("ConsultarPorPlaca", "ConsultarPorVin");
        servicio.FindMethodByName("ConsultarPorPlaca").InputType.Name.Should().Be("ConsultarPorPlacaRequest");
        servicio.FindMethodByName("ConsultarPorVin").InputType.Name.Should().Be("ConsultarPorVinRequest");
        servicio.Methods.Select(x => x.OutputType.FullName).Should()
            .AllBe($"{Paquete}.ConsultarVehiculoIntempoResponse", "G2=b: los dos rpcs devuelven la misma respuesta tipada");

        // Cliente y base generados (GrpcServices=Both).
        var tipoServicio = Contratos.GetType("Flit.Consultas.Grpc.V1.RuntIntempoService")!;
        var cliente = tipoServicio.GetNestedType("RuntIntempoServiceClient");
        var base_ = tipoServicio.GetNestedType("RuntIntempoServiceBase");
        cliente.Should().NotBeNull();
        base_.Should().NotBeNull();
        var respuesta = Contratos.GetType("Flit.Consultas.Grpc.V1.ConsultarVehiculoIntempoResponse")!;
        foreach (var (rpc, peticion) in new[] { ("ConsultarPorPlaca", "ConsultarPorPlacaRequest"), ("ConsultarPorVin", "ConsultarPorVinRequest") })
        {
            var tipoPeticion = Contratos.GetType($"Flit.Consultas.Grpc.V1.{peticion}")!;
            var asincrono = cliente!.GetMethod($"{rpc}Async", [tipoPeticion, typeof(CallOptions)]);
            asincrono.Should().NotBeNull($"el cliente generado debe exponer {rpc}Async");
            asincrono!.ReturnType.Should().Be(typeof(AsyncUnaryCall<>).MakeGenericType(respuesta));
            var servidor = base_!.GetMethod(rpc);
            servidor.Should().NotBeNull($"la base generada debe declarar {rpc}");
            servidor!.IsVirtual.Should().BeTrue();
            servidor.ReturnType.Should().Be(typeof(Task<>).MakeGenericType(respuesta));
        }

        var placa = Mensaje("ConsultarPorPlacaRequest");
        Campo(placa, "placa", 1, FieldType.String);
        placa.Fields.InFieldNumberOrder().Should().HaveCount(1);
        var vin = Mensaje("ConsultarPorVinRequest");
        Campo(vin, "vin", 1, FieldType.String);
        vin.Fields.InFieldNumberOrder().Should().HaveCount(1);

        var resp = Mensaje("ConsultarVehiculoIntempoResponse");
        Campo(resp, "proveedor", 1, FieldType.String);
        Campo(resp, "resultado", 2, FieldType.Enum, tipoMensaje: $"{Paquete}.ResultadoRuntIntempo");
        Campo(resp, "vehiculo", 3, FieldType.Message, tipoMensaje: $"{Paquete}.VehiculoIntempo");
        Campo(resp, "tipo_error", 4, FieldType.Enum, tipoMensaje: $"{Paquete}.TipoErrorFuente");
        Campo(resp, "mensaje_error", 5, FieldType.String, opcional: true);
        Campo(resp, "http_status", 6, FieldType.Int32, opcional: true);
        Campo(resp, "duracion_ms", 7, FieldType.Int32);
        Campo(resp, "errores", 8, FieldType.Message, repetido: true, tipoMensaje: $"{Paquete}.ErrorIntempo");
        resp.Fields.InFieldNumberOrder().Should().HaveCount(8, "sin respuesta cruda: trae PII");

        Archivo().EnumTypes.Single(e => e.Name == "ResultadoRuntIntempo").Values.Select(v => (v.Name, v.Number)).Should().Equal(
            ("RESULTADO_RUNT_INTEMPO_UNSPECIFIED", 0), ("RESULTADO_RUNT_INTEMPO_ENCONTRADO", 1),
            ("RESULTADO_RUNT_INTEMPO_NO_ENCONTRADO", 2), ("RESULTADO_RUNT_INTEMPO_ERROR", 3));

        var error = Mensaje("ErrorIntempo");
        Campo(error, "code", 1, FieldType.Int32);
        Campo(error, "description", 2, FieldType.String);
    }

    [Fact]
    public void VehiculoIntempo_declara_las_22_claves_de_la_seccion_5_en_proto_JSON()
    {
        var vehiculo = Mensaje("VehiculoIntempo");
        vehiculo.Fields.InFieldNumberOrder().Select(f => f.JsonName).Should().Equal(ClavesSeccion5,
            "el proto-JSON conserva las claves de §5 en su orden");

        // Toda clave con siglas lleva json_name: el proto-JSON queda igual a §5 (F10-H5 lo usa como forma de los fixtures).
        foreach (var m in Archivo().MessageTypes)
            foreach (var f in m.Fields.InFieldNumberOrder().Where(f => f.Name == "serial_version_uid"))
                f.JsonName.Should().Be("serialVersionUID", $"{m.Name}.serial_version_uid");
        var general = Mensaje("InformacionGeneralIntempo");
        general.FindFieldByName("cilidraje").Should().NotBeNull("se conserva la errata de §5.2");
        general.FindFieldByName("tiene_lt_importacion").JsonName.Should().Be("tieneLTImportacion");
        general.FindFieldByName("ver_valida_dian").JsonName.Should().Be("verValidaDIAN");
        general.FindFieldByName("validacion_dian").JsonName.Should().Be("validacionDIAN");
        foreach (var opcional in new[] { "no_motor", "puertas", "serial_version_uid" })
            general.FindFieldByName(opcional).HasPresence.Should().BeTrue($"InformacionGeneralIntempo.{opcional} puede no venir");
        var blindaje = Mensaje("InformacionBlindajeIntempo");
        blindaje.FindFieldByName("fecha_expedicion_certificado_formato_ws").JsonName.Should().Be("fechaExpedicionCertificadoFormatoWS");
        blindaje.FindFieldByName("blindado").Should().NotBeNull("un solo campo blindado (acepta esBlindado del origen)");
        blindaje.FindFieldByName("es_blindado").Should().BeNull();

        // El vehículo inventado de la HU: TST101, NIT 999990101 en leasing con un locatario C 9999901011, una dirección y un SOAT.
        var leido = JsonParser.Default.Parse(VehiculoTst101Json, vehiculo);
        var json = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true)).Format(leido);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ClavesSeccion5,
            "la raíz tiene exactamente las 22 claves de §5");
        doc.RootElement.GetProperty("informacionGeneral").TryGetProperty("serialVersionUID", out var uid).Should()
            .BeTrue("serialVersionUID conserva su nombre gracias a json_name");
        uid.GetString().Should().Be("TST101-UID");
        var propietario = doc.RootElement.GetProperty("propietariosActuales")[0];
        propietario.GetProperty("noDocumento").GetString().Should().Be("999990101");
        propietario.GetProperty("detallePropiedad").GetString().Should().Be("LEASING");
        propietario.GetProperty("locatarios")[0].GetProperty("ndoc").GetString().Should().Be("9999901011");
        propietario.GetProperty("direcciones").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("soat").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("lessee")[0].GetProperty("lesseeType").GetInt32().Should().Be(1);
    }

    /// <summary>Vehículo inventado en proto-JSON con las claves de §5 (sin datos reales).</summary>
    internal const string VehiculoTst101Json = """
        {
          "plate": "TST101", "vin": "9ZZPRUEBA00000101", "documentType": "", "documentNumber": "",
          "informacionGeneral": {
            "cilidraje": "1600", "claseVehiculo": "AUTOMOVIL", "clasificacion": "AUTOMOVIL", "color": "BLANCO",
            "linea": "LINEA PRUEBA", "marca": "MARCA PRUEBA", "modelo": "2023", "fechaMatricula": "14/04/2023",
            "noChasis": "CHASISTST101", "noLicenciaTransito": "99999000101", "noMotor": "MOTORTST101", "noPlaca": "TST101",
            "noVin": "9ZZPRUEBA00000101", "organismoTransito": "ORGANISMO PRUEBA", "puertas": "4", "tipoCarroceria": "SEDAN",
            "tipoCombustible": "GASOLINA", "tipoServicio": "Particular", "esRegrabadoChasis": "NO", "esRegrabadoMotor": "NO",
            "esRegrabadoSerie": "NO", "esRegrabadoVin": "NO", "prendas": "NO", "tieneGravamenes": "NO", "repotenciado": "NO",
            "clasicoAntiguo": "", "estadoDelVehiculo": "", "mostrarSolicitudes": "", "seguridadEstado": "", "tarjetaServicio": "",
            "verValidaDIAN": "", "validacionDIAN": "", "tieneLTImportacion": "", "serialVersionUID": "TST101-UID"
          },
          "datosTecnicos": {
            "capacidadCarga": "0", "noEjes": "2", "pesoBrutoVehicular": "1500", "pasajerosSentados": "5",
            "alto": "", "ancho": "", "largo": "", "rodaje": "", "serialVersionUID": ""
          },
          "soat": [{
            "noPoliza": "99999000001", "fechaExpedicion": "01/01/2026", "fechaVigencia": "01/01/2026",
            "fechaVencimiento": "31/12/2026", "entidadExpideSoat": "ASEGURADORA PRUEBA SA", "estado": "VIGENTE"
          }],
          "tecnoMecanica": [], "garantiasFavorDe": [], "garantiasMobiliarias": [],
          "informacionBlindaje": { "serialVersionUID": "", "blindado": "NO" },
          "limitacionPropiedad": [], "normalizacionSaneamiento": [{}], "polizasResponsabilidadCivil": [{ "serialVersionUID": "" }],
          "solicitudes": [], "tarjetaOperacion": [{ "serialVersionUID": "", "modalidadServicio": "" }],
          "propietariosActuales": [{
            "id": 7, "idTipoDocumento": "N", "tipoDocumento": "NIT", "noDocumento": "999990101",
            "nombreCompleto": "EMPRESA PRUEBA UNO SAS", "tipoPropiedad": 3, "detallePropiedad": "LEASING",
            "direcciones": [{
              "id": 11, "direccion": "CALLE FICTICIA 1 2 3", "idDepartamento": "76", "departamento": "DEPARTAMENTO PRUEBA",
              "idCiudad": "76001", "ciudad": "CIUDAD PRUEBA", "telefono": "6000000000", "email": "contacto@prueba.invalid",
              "fechaActualizacion": "01/01/2026"
            }],
            "locatarios": [{
              "tipo": 1, "tdoc": "C", "ndoc": "9999901011", "nombreRazonSocial": "PERSONA PRUEBA UNO", "direcciones": []
            }]
          }],
          "historicoPropietarios": [],
          "owner": [{
            "documentType": "NIT", "documentNumber": "999990101", "name": "EMPRESA PRUEBA UNO SAS", "firstName": "EMPRESA",
            "secondName": "PRUEBA", "firstLastName": "UNO", "secondLastName": "SAS"
          }],
          "lessee": [{
            "documentType": "C", "documentNumber": "9999901011", "name": "PERSONA PRUEBA UNO", "firstName": "PERSONA",
            "secondName": "", "firstLastName": "PRUEBA", "secondLastName": "UNO", "lesseeType": 1
          }],
          "accidentRate": [{}], "infracciones": []
        }
        """;
}
