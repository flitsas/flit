using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13873 (F10-H1) — el contrato <c>flit.consultas.v1.RuntIntempoService</c> visto desde el diagnóstico: la respuesta
/// <c>ConsultarVehiculoIntempoResponse</c> (ENCONTRADO con propietario en leasing y locatario, y ERROR con
/// <c>tipo_error=TIMEOUT</c> y <c>errores</c>) va y vuelve en binario y en proto-JSON sin perder nada, y mientras
/// core-consultas no implemente el servicio (F10-H4) los dos rpcs responden UNIMPLEMENTED sin afectar a
/// <c>ConsultasService</c>. Las pruebas usan el descriptor generado, sin tipos de compilación del RUNT: compilan antes del
/// proto. Datos inventados (TST###, 99999…).
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~ContratoRuntIntempoProtoTests"
/// </code>
/// </example>
public sealed class ContratoRuntIntempoProtoTests
{
    private const string Servicio = "flit.consultas.v1.RuntIntempoService";

    private static FileDescriptor Archivo()
    {
        var reflexion = typeof(ConsultasService).Assembly.GetType("Flit.Consultas.Grpc.V1.RuntIntempoReflection");
        reflexion.Should().NotBeNull("el contrato debe declarar runt_intempo.proto");
        return (FileDescriptor)reflexion!.GetProperty("Descriptor")!.GetValue(null)!;
    }

    private static MessageDescriptor Respuesta() =>
        Archivo().MessageTypes.Single(m => m.Name == "ConsultarVehiculoIntempoResponse");

    private static readonly JsonFormatter Formateador = new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));

    [Fact]
    public void La_respuesta_con_propietario_leasing_y_locatario_ida_y_vuelta_conserva_todo()
    {
        var d = Respuesta();

        // ENCONTRADO: propietario NIT 999990101 en leasing, un locatario C 9999901011 cuya dirección no trae correo.
        var encontrada = JsonParser.Default.Parse(EncontradaJson, d);
        var binaria = d.Parser.ParseFrom(encontrada.ToByteArray());
        binaria.Should().Be(encontrada, "binario ida y vuelta");
        var deJson = JsonParser.Default.Parse(Formateador.Format(binaria), d);
        deJson.Should().Be(encontrada, "proto-JSON ida y vuelta (forma de los fixtures de F10-H5)");

        var vehiculo = (IMessage)d.FindFieldByName("vehiculo").Accessor.GetValue(binaria);
        var propietario = Elementos(vehiculo, "propietarios_actuales").Single();
        Valor(propietario, "id").Should().BeOfType<long>().And.Be(7L);
        Valor(propietario, "tipo_propiedad").Should().BeOfType<int>().And.Be(3);
        var direccion = Elementos(propietario, "direcciones").Single();
        Valor(direccion, "id").Should().BeOfType<long>().And.Be(11L);
        Valor(direccion, "id_departamento").Should().Be("76", "id_departamento viaja como texto");
        Valor(direccion, "id_ciudad").Should().Be("");
        var locatario = Elementos(propietario, "locatarios").Single();
        Valor(locatario, "tipo").Should().Be(1);
        var dirLocatario = Elementos(locatario, "direcciones").Single();
        Presente(dirLocatario, "email").Should().BeFalse("los locatarios no traen correo: optional ausente");
        Presente(dirLocatario, "fecha_actualizacion").Should().BeFalse();
        var lessee = Elementos(vehiculo, "lessee").Single();
        Presente(lessee, "lessee_type").Should().BeTrue();
        Valor(lessee, "lessee_type").Should().Be(1);
        Presente(Elementos(vehiculo, "owner").Single(), "lessee_type").Should().BeFalse("el propietario no tiene lesseeType");

        using (var doc = JsonDocument.Parse(Formateador.Format(binaria)))
        {
            doc.RootElement.GetProperty("resultado").GetString().Should().Be("RESULTADO_RUNT_INTEMPO_ENCONTRADO");
            doc.RootElement.GetProperty("vehiculo").GetProperty("informacionGeneral").GetProperty("serialVersionUID").GetString()
                .Should().Be("TST102-UID");
        }

        // ERROR: TIMEOUT con errores [{code:404, description}] (§5.6).
        var error = JsonParser.Default.Parse(ErrorJson, d);
        var errorLeida = d.Parser.ParseFrom(error.ToByteArray());
        errorLeida.Should().Be(error);
        JsonParser.Default.Parse(Formateador.Format(errorLeida), d).Should().Be(error);
        Valor(errorLeida, "resultado").ToString().Should().Be("Error");
        Valor(errorLeida, "tipo_error").ToString().Should().Be("Timeout");
        Presente(errorLeida, "vehiculo").Should().BeFalse();
        Valor(errorLeida, "http_status").Should().Be(504);
        var e = Elementos(errorLeida, "errores").Single();
        (Valor(e, "code"), Valor(e, "description")).Should().Be((404, "AxiosError: Request failed with status code 404"));
    }

    [Fact]
    public async Task Los_dos_rpcs_sin_implementar_responden_UNIMPLEMENTED_y_no_afectan_ConsultasService()
    {
        var servicio = Archivo().Services.SingleOrDefault(s => s.FullName == Servicio);
        servicio.Should().NotBeNull("el contrato debe declarar RuntIntempoService");
        servicio!.Methods.Select(m => m.Name).Should().BeEquivalentTo("ConsultarPorPlaca", "ConsultarPorVin");

        // Como core-consultas hasta F10-H4: atiende ConsultasService y no mapea RuntIntempoService.
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Services.AddGrpc();
        await using var app = b.Build();
        app.MapGrpcService<ConsultasSinRuntIntempo>();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
        var invocador = canal.CreateCallInvoker();

        foreach (var (metodo, campo, valor) in new[] { ("ConsultarPorPlaca", "placa", "TST103"), ("ConsultarPorVin", "vin", "9ZZPRUEBA00000103") })
        {
            var peticionDesc = servicio.FindMethodByName(metodo).InputType;
            var peticion = (IMessage)Activator.CreateInstance(peticionDesc.ClrType)!;
            peticionDesc.FindFieldByName(campo).Accessor.SetValue(peticion, valor);
            var cuerpo = peticion.ToByteArray();
            var llamada = () => invocador.AsyncUnaryCall(Crudo(metodo), null, new CallOptions(), cuerpo).ResponseAsync;
            (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented, metodo);
        }

        var rues = await new ConsultasService.ConsultasServiceClient(canal)
            .ConsultarRuesAsync(new ConsultarRuesRequest { Nit = "999990101" }, cancellationToken: TestContext.Current.CancellationToken);
        rues.Resultado.Proveedor.Should().Be("falso", "ConsultasService sigue respondiendo");
    }

    private static object Valor(IMessage m, string campo) => m.Descriptor.FindFieldByName(campo).Accessor.GetValue(m);

    private static bool Presente(IMessage m, string campo) => m.Descriptor.FindFieldByName(campo).Accessor.HasValue(m);

    private static List<IMessage> Elementos(IMessage m, string campo) =>
        ((System.Collections.IList)Valor(m, campo)).Cast<IMessage>().ToList();

    private static Method<byte[], byte[]> Crudo(string metodo)
    {
        var bytes = Marshallers.Create(x => x, x => x);
        return new Method<byte[], byte[]>(MethodType.Unary, Servicio, metodo, bytes, bytes);
    }

    private sealed class ConsultasSinRuntIntempo : ConsultasService.ConsultasServiceBase
    {
        public override Task<ConsultarRuesResponse> ConsultarRues(ConsultarRuesRequest request, ServerCallContext context) =>
            Task.FromResult(new ConsultarRuesResponse { Resultado = new ResultadoConsulta { Proveedor = "falso" } });
    }

    private const string EncontradaJson = """
        {
          "proveedor": "intempo", "resultado": "RESULTADO_RUNT_INTEMPO_ENCONTRADO", "duracionMs": 120, "httpStatus": 200,
          "vehiculo": {
            "plate": "TST102", "vin": "9ZZPRUEBA00000102", "documentType": "", "documentNumber": "",
            "informacionGeneral": { "cilidraje": "2000", "noPlaca": "TST102", "tipoServicio": "Particular", "serialVersionUID": "TST102-UID" },
            "datosTecnicos": { "noEjes": "2", "pasajerosSentados": "5" },
            "soat": [{ "noPoliza": "99999000002", "fechaVencimiento": "31/12/2026", "estado": "VIGENTE" }],
            "informacionBlindaje": { "blindado": "NO" },
            "normalizacionSaneamiento": [{}], "accidentRate": [{}],
            "propietariosActuales": [{
              "id": 7, "idTipoDocumento": "N", "tipoDocumento": "NIT", "noDocumento": "999990101",
              "nombreCompleto": "EMPRESA PRUEBA UNO SAS", "tipoPropiedad": 3, "detallePropiedad": "LEASING",
              "direcciones": [{
                "id": 11, "direccion": "CALLE FICTICIA 1 2 3", "idDepartamento": "76", "departamento": "DEPARTAMENTO PRUEBA",
                "idCiudad": "", "ciudad": "CIUDAD PRUEBA", "telefono": "6000000000", "email": "contacto@prueba.invalid",
                "fechaActualizacion": "01/01/2026"
              }],
              "locatarios": [{
                "tipo": 1, "tdoc": "C", "ndoc": "9999901011", "nombreRazonSocial": "PERSONA PRUEBA UNO",
                "direcciones": [{ "id": 12, "direccion": "CARRERA FICTICIA 4 5 6", "idDepartamento": "", "idCiudad": "", "ciudad": "", "telefono": "" }]
              }]
            }],
            "historicoPropietarios": [{
              "nombreCompletoPropietario": "EMPRESA PRUEBA UNO SAS", "tipoDocumento": "NIT", "numeroDocumento": "999990101",
              "fechaInicioPropiedad": "14/04/2023", "direccion": { "id": 11, "direccion": "CALLE FICTICIA 1 2 3", "idDepartamento": "76" }
            }],
            "owner": [{ "documentType": "NIT", "documentNumber": "999990101", "name": "EMPRESA PRUEBA UNO SAS" }],
            "lessee": [{ "documentType": "C", "documentNumber": "9999901011", "name": "PERSONA PRUEBA UNO", "lesseeType": 1 }],
            "infracciones": [{ "nroComparendo": "99999000000000000001", "valorInfraccion": "604100.00", "placa": "TST102", "estado": "PENDIENTE" }]
          }
        }
        """;

    private const string ErrorJson = """
        {
          "proveedor": "intempo", "resultado": "RESULTADO_RUNT_INTEMPO_ERROR", "tipoError": "TIPO_ERROR_FUENTE_TIMEOUT",
          "mensajeError": "Intempo no respondió a tiempo.", "httpStatus": 504, "duracionMs": 20000,
          "errores": [{ "code": 404, "description": "AxiosError: Request failed with status code 404" }]
        }
        """;
}
