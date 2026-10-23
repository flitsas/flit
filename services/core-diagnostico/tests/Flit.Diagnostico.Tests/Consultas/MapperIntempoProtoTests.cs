using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Xunit;
using TipoErrorDiagnostico = Flit.Diagnostico.Api.Modulos.Consultas.Fuentes.TipoErrorFuente;
using TipoErrorIntempo = Flit.Consultas.Grpc.V1.TipoErrorFuente;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13877 (F10-H5) — <see cref="MapperIntempo"/> convierte la respuesta tipada del RUNT por Intempo
/// (<c>ConsultarVehiculoIntempoResponse</c> de core-consultas) en <see cref="VehiculoRunt"/> por una sola ruta, la misma de la
/// fuente simulada (proto-JSON con <c>JsonParser</c>) y de la real (mensaje ya tipado): cilindraje de <c>cilidraje</c>,
/// pasajeros de <c>datosTecnicos.pasajerosSentados</c>, tipo de documento del propietario por <c>idTipoDocumento</c> con caída
/// al texto, locatarios de <c>lessee</c> en texto sin duplicar, <c>IdTransaccion</c> nulo, sin nombres ni direcciones, y los
/// errores tipados traducidos a códigos que conoce <see cref="ClasificadorErrores"/>. Datos inventados (TST5##, 99999…).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~MapperIntempoProtoTests
/// </code>
/// </example>
public sealed class MapperIntempoProtoTests
{
    private const string Servicio = "INTEMPO";

    // Nombres, direcciones, teléfonos y correos inventados: nunca deben salir del mapper.
    private static readonly string[] DatosPersonales =
    [
        "PROPIETARIO FICTICIO QWZ", "LOCATARIA FICTICIA QWZ", "DUENO FICTICIO QWZ", "NOMBRE PILA QWZ", "APELLIDO QWZ",
        "HISTORICO FICTICIO QWZ", "CALLE FICTICIA 1 QWZ", "CARRERA INVENTADA 2 QWZ", "3000000000", "correo@ejemplo.test",
        "CIUDAD QWZ",
    ];

    /// <summary>AC1: TST501, propietario N en leasing con locatario C, y el mismo locatario en <c>lessee</c> con el tipo en texto.</summary>
    internal const string EncontradoTst501 = """
        {
          "proveedor": "intempo",
          "resultado": "RESULTADO_RUNT_INTEMPO_ENCONTRADO",
          "httpStatus": 200,
          "duracionMs": 140,
          "vehiculo": {
            "plate": "TST501",
            "informacionGeneral": {
              "noPlaca": "TST501",
              "noVin": "9ZZPRUEBA00000501",
              "tipoServicio": "PARTICULAR",
              "claseVehiculo": "AUTOMOVIL",
              "organismoTransito": "SECRETARIA DE TRANSITO DE PRUEBA",
              "marca": "MARCA PRUEBA",
              "linea": "LINEA PRUEBA",
              "modelo": "2021",
              "cilidraje": "1598",
              "tipoCombustible": "GASOLINA"
            },
            "datosTecnicos": { "pasajerosSentados": "5" },
            "propietariosActuales": [
              {
                "id": "7",
                "idTipoDocumento": "N",
                "tipoDocumento": "NIT",
                "noDocumento": "999990501",
                "nombreCompleto": "PROPIETARIO FICTICIO QWZ",
                "tipoPropiedad": 3,
                "detallePropiedad": "LEASING",
                "direcciones": [
                  { "id": "1", "direccion": "CALLE FICTICIA 1 QWZ", "ciudad": "CIUDAD QWZ", "telefono": "3000000000", "email": "correo@ejemplo.test" }
                ],
                "locatarios": [
                  {
                    "tipo": 1,
                    "tdoc": "C",
                    "ndoc": "9999905011",
                    "nombreRazonSocial": "LOCATARIA FICTICIA QWZ",
                    "direcciones": [ { "direccion": "CARRERA INVENTADA 2 QWZ" } ]
                  }
                ]
              }
            ],
            "historicoPropietarios": [
              { "nombreCompletoPropietario": "HISTORICO FICTICIO QWZ", "tipoDocumento": "NIT", "numeroDocumento": "999990599", "direccion": { "direccion": "CALLE FICTICIA 1 QWZ" } }
            ],
            "owner": [
              { "documentType": "NIT", "documentNumber": "999990501", "name": "DUENO FICTICIO QWZ", "firstName": "NOMBRE PILA QWZ", "firstLastName": "APELLIDO QWZ" }
            ],
            "lessee": [
              { "documentType": "Cédula de ciudadanía", "documentNumber": "9999905011", "name": "LOCATARIA FICTICIA QWZ", "lesseeType": 1 }
            ]
          }
        }
        """;

    private static VehiculoRunt Vehiculo(string json)
    {
        var resultado = MapperIntempo.MapearJson(json, Servicio);
        resultado.Exitoso.Should().BeTrue(resultado.Mensaje);
        return resultado.Vehiculo!;
    }

    private static ConsultarVehiculoIntempoResponse Respuesta() =>
        JsonParserIntempo.Parse(EncontradoTst501);

    // AC1 (F10-C9) — datos técnicos de las claves reales: cilidraje y datosTecnicos.pasajerosSentados.
    [Fact]
    public void Encontrado_toma_cilidraje_y_pasajeros_de_datos_tecnicos()
    {
        var vehiculo = Vehiculo(EncontradoTst501);

        (vehiculo.Placa, vehiculo.Vin, vehiculo.Servicio, vehiculo.Clase, vehiculo.Organismo)
            .Should().Be(("TST501", "9ZZPRUEBA00000501", "PARTICULAR", "AUTOMOVIL", "SECRETARIA DE TRANSITO DE PRUEBA"));
        (vehiculo.Marca, vehiculo.Linea, vehiculo.Modelo, vehiculo.Cilindraje, vehiculo.Combustible, vehiculo.Pasajeros)
            .Should().Be(("MARCA PRUEBA", "LINEA PRUEBA", (int?)2021, (int?)1598, "GASOLINA", (int?)5));
        vehiculo.ServicioQueResolvio.Should().Be(Servicio);

        // La ruta tipada (fuente real) da exactamente lo mismo que la de proto-JSON (fuente simulada).
        var tipado = MapperIntempo.Mapear(Respuesta(), Servicio).Vehiculo!;
        JsonSerializer.Serialize(tipado).Should().Be(JsonSerializer.Serialize(vehiculo));

        // Sin plate en la raíz, la placa sale de informacionGeneral.noPlaca; sin datos técnicos, nulos.
        var sinRaiz = Respuesta();
        sinRaiz.Vehiculo.Plate = string.Empty;
        sinRaiz.Vehiculo.DatosTecnicos = null;
        sinRaiz.Vehiculo.InformacionGeneral.Cilidraje = string.Empty;
        var r = MapperIntempo.Mapear(sinRaiz, Servicio).Vehiculo!;
        (r.Placa, r.Cilindraje, r.Pasajeros).Should().Be(("TST501", (int?)null, (int?)null));
    }

    // AC1 (F10-C9) — el tipo del propietario sale de idTipoDocumento (C/N); sin él, del texto por el mapa inverso.
    [Fact]
    public void Propietario_usa_idTipoDocumento_y_cae_al_texto()
    {
        var propietario = Vehiculo(EncontradoTst501).Propietarios.Should().ContainSingle().Subject;
        (propietario.TipoDocumento, propietario.NumeroDocumento, propietario.TipoPropiedad).Should().Be(("NIT", "999990501", TipoPropiedad.Leasing));
        propietario.Locatarios.Should().Equal(new LocatarioRunt("CC", "9999905011"));

        var respuesta = Respuesta();
        var p = respuesta.Vehiculo.PropietariosActuales[0];
        p.IdTipoDocumento = "N";
        p.TipoDocumento = "Cédula Ciudadanía";
        MapperIntempo.Mapear(respuesta, Servicio).Vehiculo!.Propietarios[0].TipoDocumento.Should().Be("NIT", "manda idTipoDocumento");

        foreach (var (texto, esperado) in new[]
                 {
                     ("Cédula Ciudadanía", "CC"), ("CEDULA DE CIUDADANIA", "CC"), ("NIT", "NIT"), ("Cédula de extranjería", "CE"),
                     ("Pasaporte", "PAS"), ("Tarjeta de identidad", "TI"),
                 })
        {
            p.IdTipoDocumento = string.Empty;
            p.TipoDocumento = texto;
            MapperIntempo.Mapear(respuesta, Servicio).Vehiculo!.Propietarios[0].TipoDocumento.Should().Be(esperado, texto);
        }

        // Propiedad por tipo_propiedad cuando falta detalle_propiedad; sin número de documento, el propietario no cuenta.
        p.DetallePropiedad = string.Empty;
        p.TipoPropiedad = 1;
        MapperIntempo.Mapear(respuesta, Servicio).Vehiculo!.Propietarios[0].TipoPropiedad.Should().Be(TipoPropiedad.Propio);
        p.NoDocumento = " ";
        MapperIntempo.Mapear(respuesta, Servicio).Vehiculo!.Propietarios.Should().BeEmpty();
    }

    // AC1 (F10-C9) — lessee[].documentType en texto se normaliza antes de deduplicar: un solo locatario.
    [Fact]
    public void Locatario_en_lessee_con_texto_no_se_duplica_con_el_del_propietario()
    {
        Vehiculo(EncontradoTst501).Locatarios.Should().Equal(new LocatarioRunt("CC", "9999905011"));

        var respuesta = Respuesta();
        respuesta.Vehiculo.Lessee[0].DocumentType = "CEDULA CIUDADANIA";
        respuesta.Vehiculo.Lessee[0].DocumentNumber = " 9999905011 ";
        respuesta.Vehiculo.Lessee.Add(new PersonaIntempo { DocumentType = "Cédula Ciudadanía", DocumentNumber = "9999905011" });
        respuesta.Vehiculo.Lessee.Add(new PersonaIntempo { DocumentType = "NIT", DocumentNumber = "999990502" });
        respuesta.Vehiculo.Lessee.Add(new PersonaIntempo { DocumentType = "NIT", DocumentNumber = string.Empty });

        MapperIntempo.Mapear(respuesta, Servicio).Vehiculo!.Locatarios
            .Should().BeEquivalentTo([new LocatarioRunt("CC", "9999905011"), new LocatarioRunt("NIT", "999990502")]);
    }

    // AC1 (F10-C9) — Intempo no informa transacción y el id de consumo de core-consultas no viaja: IdTransaccion nulo.
    [Fact]
    public void IdTransaccion_es_nulo()
    {
        Vehiculo(EncontradoTst501).IdTransaccion.Should().BeNull();
        MapperIntempo.Mapear(Respuesta(), Servicio).Vehiculo!.IdTransaccion.Should().BeNull();
    }

    // AC4 (F10-C9, R-20, Ley 1581) — nombres, direcciones, teléfonos y correos nunca llegan a VehiculoRunt.
    [Fact]
    public void Nunca_expone_nombres_ni_direcciones()
    {
        var resultado = MapperIntempo.MapearJson(EncontradoTst501, Servicio);
        var json = JsonSerializer.Serialize(resultado.Vehiculo);

        json.Should().NotContainAny(DatosPersonales);
        json.Should().NotContain("999990599", "el histórico de propietarios no se lee");

        var prohibidas = new[] { "Nombre", "Name", "RazonSocial", "Apellido", "Direccion", "Telefono", "Email", "Correo", "Historico" };
        Propiedades(typeof(VehiculoRunt), []).Should().NotContain(n => prohibidas.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase)));
    }

    // AC3 (F10-C10, G3=a) — vehículo no registrado: ESTADO_NO_OK definitivo con mensaje fijo.
    [Fact]
    public void No_encontrado_es_ESTADO_NO_OK_definitivo_con_mensaje_fijo()
    {
        var resultado = MapperIntempo.MapearJson(
            """{"proveedor":"intempo","resultado":"RESULTADO_RUNT_INTEMPO_NO_ENCONTRADO","httpStatus":404,"duracionMs":80}""", Servicio);

        resultado.Vehiculo.Should().BeNull();
        resultado.Error.Should().Be(TipoErrorDiagnostico.EstadoNoOk);
        resultado.EsTransitorio.Should().BeFalse("no se reintenta lo que el RUNT no registra");
        resultado.Mensaje.Should().Be("El RUNT no registra el vehículo consultado.").And.Be(MapperIntempo.MensajeNoEncontrado);
        TipoErrorFuenteCodigos.Codigo(resultado.Error!.Value).Should().Be("ESTADO_NO_OK");
    }

    // AC3 (F10-C10) — errores tipados de core-consultas: HTTP 5xx (o sin status) transitorio, 4xx ESTADO_NO_OK; nunca se copia
    // mensaje_error (puede traer un recorte del cuerpo).
    [Theory]
    [InlineData(TipoErrorIntempo.Timeout, null, TipoErrorDiagnostico.Timeout, true)]
    [InlineData(TipoErrorIntempo.Red, null, TipoErrorDiagnostico.Red, true)]
    [InlineData(TipoErrorIntempo.HttpNoExitoso, 502, TipoErrorDiagnostico.Http5xx, true)]
    [InlineData(TipoErrorIntempo.HttpNoExitoso, null, TipoErrorDiagnostico.Http5xx, true)]
    [InlineData(TipoErrorIntempo.HttpNoExitoso, 400, TipoErrorDiagnostico.EstadoNoOk, false)]
    [InlineData(TipoErrorIntempo.Autenticacion, 401, TipoErrorDiagnostico.Autenticacion, false)]
    [InlineData(TipoErrorIntempo.RespuestaInvalida, 200, TipoErrorDiagnostico.RespuestaInvalida, false)]
    [InlineData(TipoErrorIntempo.EstadoNoOk, 200, TipoErrorDiagnostico.EstadoNoOk, false)]
    [InlineData(TipoErrorIntempo.SoapFault, null, TipoErrorDiagnostico.EstadoNoOk, false)]
    [InlineData(TipoErrorIntempo.Excepcion, null, TipoErrorDiagnostico.EstadoNoOk, false)]
    [InlineData(TipoErrorIntempo.Unspecified, null, TipoErrorDiagnostico.EstadoNoOk, false)]
    public void Errores_tipados_se_traducen_y_HTTP_5xx_es_transitorio(TipoErrorIntempo tipo, int? status, TipoErrorDiagnostico esperado, bool transitorio)
    {
        var respuesta = new ConsultarVehiculoIntempoResponse
        {
            Proveedor = "intempo",
            Resultado = ResultadoRuntIntempo.Error,
            TipoError = tipo,
            MensajeError = "Intempo respondió con CUERPO-SECRETO-TST501 9999905011",
        };
        if (status is { } s)
            respuesta.HttpStatus = s;
        respuesta.Errores.Add(new ErrorIntempo { Code = status ?? 404, Description = "CUERPO-SECRETO-TST501" });

        var resultado = MapperIntempo.Mapear(respuesta, Servicio);

        resultado.Vehiculo.Should().BeNull();
        resultado.Error.Should().Be(esperado);
        resultado.EsTransitorio.Should().Be(transitorio);
        resultado.Mensaje.Should().NotBeNullOrWhiteSpace().And.NotContain("CUERPO-SECRETO").And.NotContain("9999905011").And.NotContain("TST501");
        ClasificadorErrores.Clasificar(TipoErrorFuenteCodigos.Codigo(esperado))
            .Should().Be(transitorio ? ClaseError.Transitorio : ClaseError.Definitivo);
    }

    // AC3 (F10-C10) — cada valor de TipoErrorFuente tiene un código de la tabla del clasificador y de los textos del tablero
    // (HTTP_5XX, AUTENTICACION, ESTADO_NO_OK); CodigosDeEnum no da HTTP_5XX, por eso el mapa explícito.
    [Fact]
    public void Codigos_de_error_RUNT_estan_en_la_tabla_del_clasificador()
    {
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.Http5xx).Should().Be("HTTP_5XX");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.Autenticacion).Should().Be("AUTENTICACION");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.EstadoNoOk).Should().Be("ESTADO_NO_OK");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.Timeout).Should().Be("TIMEOUT");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.Red).Should().Be("RED");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.RespuestaInvalida).Should().Be("RESPUESTA_INVALIDA");
        TipoErrorFuenteCodigos.Codigo(TipoErrorDiagnostico.NoConectado).Should().Be("NO_CONECTADO");

        foreach (var tipo in Enum.GetValues<TipoErrorDiagnostico>())
        {
            var codigo = TipoErrorFuenteCodigos.Codigo(tipo);
            ClasificadorErrores.EsConocido(codigo).Should().BeTrue(codigo);
            var transitorio = ResultadoRunt.Fallo(tipo, "x").EsTransitorio;
            ClasificadorErrores.Clasificar(codigo).Should().Be(transitorio ? ClaseError.Transitorio : ClaseError.Definitivo, codigo);
        }
    }

    // AC3 (F10-C10) — la celda RUNT guarda el código del mapa explícito (HTTP_5XX, AUTENTICACION, ESTADO_NO_OK), no el de
    // CodigosDeEnum. Sin reintentos para que el transitorio quede en ERROR al primer intento. PostgreSQL real.
    [Fact]
    public async Task Celda_RUNT_guarda_el_codigo_del_mapa_explicito()
    {
        var ct = TestContext.Current.CancellationToken;
        var fuente = new FuenteRuntDeErrores(new Dictionary<string, ResultadoRunt>
        {
            ["TST511"] = MapperIntempo.Mapear(Error(TipoErrorIntempo.HttpNoExitoso, 502), Servicio),
            ["TST512"] = MapperIntempo.Mapear(Error(TipoErrorIntempo.Autenticacion, 401), Servicio),
            ["TST513"] = MapperIntempo.MapearJson("""{"proveedor":"intempo","resultado":"RESULTADO_RUNT_INTEMPO_NO_ENCONTRADO"}""", Servicio),
        });
        await using var api = await MotorRunt.ArrancarAsync(b => new FuenteRuntEspia(new ConcurrentQueue<string>(), fuente).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST511"), Lotes.Fila("TST512"), Lotes.Fila("TST513")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        var vehiculos = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).ToDictionary(v => v.Id, v => v.IdentificadorNorm);
        var codigos = (await Motor.CeldasAsync(api, empresa.Id, id, ct))
            .Where(c => c.Fuente == Fuente.Runt)
            .ToDictionary(c => vehiculos[c.VehiculoId], c => (c.Estado, c.TipoError));
        codigos.Should().BeEquivalentTo(new Dictionary<string, (EstadoCelda, string?)>
        {
            ["TST511"] = (EstadoCelda.Error, "HTTP_5XX"),
            ["TST512"] = (EstadoCelda.Error, "AUTENTICACION"),
            ["TST513"] = (EstadoCelda.Error, "ESTADO_NO_OK"),
        });
        codigos.Values.Select(v => v.TipoError).Should().OnlyContain(c => ClasificadorErrores.EsConocido(c));
    }

    // AC4 (F10-C9) — la forma vieja (codigoResultado, codigoUUID) o cualquier clave desconocida es RespuestaInvalida.
    [Theory]
    [InlineData("""{"codigoResultado":"Exitoso","informacionGeneral":{"noPlaca":"TST501"}}""")]
    [InlineData("""{"resultado":"RESULTADO_RUNT_INTEMPO_ENCONTRADO","codigoUUID":"00000000-0000-4000-8000-000000000501"}""")]
    [InlineData("""{"resultado":"RESULTADO_RUNT_INTEMPO_ENCONTRADO","vehiculo":{"plate":"TST501","claveInventada":"x"}}""")]
    [InlineData("""{"resultado":"RESULTADO_RUNT_INTEMPO_ENCONTRADO","vehiculo":{"informacionGeneral":{"cilindraje":"1598"}}}""")]
    [InlineData("no es json")]
    [InlineData("{}")]
    [InlineData("""{"resultado":"RESULTADO_RUNT_INTEMPO_ENCONTRADO"}""")]
    public void ProtoJson_con_clave_desconocida_es_RespuestaInvalida(string json)
    {
        var resultado = MapperIntempo.MapearJson(json, Servicio);

        resultado.Vehiculo.Should().BeNull();
        resultado.Error.Should().Be(TipoErrorDiagnostico.RespuestaInvalida);
        resultado.EsTransitorio.Should().BeFalse();
        resultado.Mensaje.Should().NotContain("TST501");
        MapperIntempo.Mapear(json, Servicio).Error.Should().Be(TipoErrorDiagnostico.RespuestaInvalida, "la firma vieja llama a MapearJson");
    }

    private static ConsultarVehiculoIntempoResponse Error(TipoErrorIntempo tipo, int status) => new()
    {
        Proveedor = "intempo",
        Resultado = ResultadoRuntIntempo.Error,
        TipoError = tipo,
        HttpStatus = status,
    };

    private static IEnumerable<string> Propiedades(Type tipo, HashSet<Type> vistos)
    {
        if (!vistos.Add(tipo))
            yield break;
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return $"{tipo.Name}.{p.Name}";
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t.IsGenericType && t.GetGenericArguments() is [var elemento])
                t = elemento;
            if (t.Namespace == typeof(VehiculoRunt).Namespace && t.IsClass)
            {
                foreach (var anidada in Propiedades(t, vistos))
                    yield return anidada;
            }
        }
    }
}

/// <summary>Lector proto-JSON estricto de la respuesta tipada del RUNT por Intempo, para armar casos en las pruebas.</summary>
internal static class JsonParserIntempo
{
    private static readonly Google.Protobuf.JsonParser Parser = new(Google.Protobuf.JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    public static ConsultarVehiculoIntempoResponse Parse(string json) => Parser.Parse<ConsultarVehiculoIntempoResponse>(json);
}

/// <summary>Fuente RUNT de prueba (HU #13877): devuelve el resultado ya mapeado de cada placa.</summary>
internal sealed class FuenteRuntDeErrores(IReadOnlyDictionary<string, ResultadoRunt> porPlaca) : IFuenteRunt
{
    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct) =>
        Task.FromResult(porPlaca[identificador.Valor]);
}
