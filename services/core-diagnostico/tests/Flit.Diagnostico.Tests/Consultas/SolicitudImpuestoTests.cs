using System.Net.Http.Json;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Configuracion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// C3-6: la solicitud de impuesto lleva el primer propietario, con nombre y documento de la misma persona, y cierra con un
/// código visible si falta un dato.
/// No pasa por el bus del RUNT: siembra el vehículo, la réplica y la respuesta cifrada.
/// </summary>
public sealed class SolicitudImpuestoTests
{
    private const string Departamento = "05";
    private const string Documento = "9999900001";
    private const string Nombre = "PRIMERO DE PRUEBA";

    private const string JsonConDosNombres = """
        {"vehiculo":{"propietarios_actuales":[
          {"id_tipo_documento":"C","no_documento":"9999900001","nombre_completo":"PRIMERO DE PRUEBA"},
          {"id_tipo_documento":"C","no_documento":"9999900002","nombre_completo":"OTRO"}]}}
        """;

    [Fact]
    public void Servicio_publico_no_aplica_antes_de_armar_la_solicitud() =>
        ReglasAplicabilidad.Evaluar(Fuente.ImpuestoDepartamental, new HechosAplicabilidad("PUBLICO", Departamento, true, null))
            .Should().Be(MotivoNa.ServicioPublico);

    [Fact]
    public async Task La_solicitud_lleva_el_primer_propietario_sin_homologar_el_tipo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, tipo: "pas", numero: "AB-100", json: Json("TITULAR PAS", "PAS", "AB-100"));

        var solicitud = await UnaAsync(api, sembrado, ct);

        solicitud.Documento.Tipo.Should().Be("PAS");
        solicitud.Documento.Numero.Should().Be("AB-100");
        solicitud.PropietarioNombre.Should().Be("TITULAR PAS");
        solicitud.Modelo.Should().Be(2020);
        solicitud.Municipio.Should().Be("MUNICIPIO DE PRUEBA");
        solicitud.DepartamentoNombre.Should().Be("DEPARTAMENTO DE PRUEBA");
    }

    [Fact]
    public async Task El_nombre_es_el_del_orden_1_y_el_nit_va_sin_digito()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, tipo: "nit", numero: "999990003-1", json: Json(Nombre, "N", "999990003-1"));

        var solicitud = await UnaAsync(api, sembrado, ct);

        solicitud.Documento.Should().BeEquivalentTo(new { Tipo = "NIT", Numero = "999990003" });
        solicitud.PropietarioNombre.Should().Be(Nombre);
    }

    [Fact]
    public async Task Un_propietario_recien_registrado_en_el_contexto_se_ve()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, persona: false);
        await using var db = sembrado.Empresa.Db();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == sembrado.ImpuestoId, ct);
        db.Add(Persona(api, sembrado.Empresa.Id, sembrado.VehiculoId, "CC", Documento));

        var solicitud = (ConsultarImpuestoVehicularRequest)(await ConstruirAsync(api, db, celda, ct)).Single().Solicitud;

        solicitud.Documento.Numero.Should().Be(Documento);
        (await db.Set<PersonaVehiculo>().CountAsync(ct)).Should().Be(0, "todavía no se guardó");
    }

    [Theory]
    [InlineData("sin-placa", EjecutorCeldaImpuesto.SinPlaca)]
    [InlineData("sin-documento", EjecutorCeldaImpuesto.SinDocumento)]
    [InlineData("sin-modelo", EjecutorCeldaImpuesto.SinModelo)]
    [InlineData("organismo", EjecutorCeldaImpuesto.OrganismoIncompleto)]
    [InlineData("sin-nombre", EjecutorCeldaImpuesto.SinNombrePropietario)]
    public async Task Cada_falta_cierra_con_su_codigo_y_no_publica(string falta, string codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct,
            placa: falta != "sin-placa",
            persona: falta != "sin-documento",
            modelo: falta != "sin-modelo",
            municipio: falta != "organismo",
            json: falta == "sin-nombre" ? """{"vehiculo":{"propietarios_actuales":[{}]}}""" : JsonConDosNombres);
        await using var db = sembrado.Empresa.Db();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == sembrado.ImpuestoId, ct);
        var log = new ListaLog();

        (await ConstruirAsync(api, db, celda, ct)).Should().BeEmpty();
        var cierre = await new EjecutorCeldaImpuesto(Docs(api), Respuestas(api), log).SinConsultaAsync(new ContextoCelda(CeldaEnCurso.De(celda), db), ct);

        cierre.Estado.Should().Be(EstadoCelda.Error);
        cierre.TipoError.Should().Be(codigo);
        string.Join('\n', log.Textos).Should().NotContain(Documento).And.NotContain(Nombre);
    }

    [Fact]
    public async Task Departamento_fuera_de_la_replica_queda_na_sin_solicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, departamento: "11", soportar: false);
        await using var db = sembrado.Empresa.Db();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == sembrado.ImpuestoId, ct);

        (await ConstruirAsync(api, db, celda, ct)).Should().BeEmpty();
        var cierre = await new EjecutorCeldaImpuesto(Docs(api), Respuestas(api), Microsoft.Extensions.Logging.Abstractions.NullLogger<EjecutorCeldaImpuesto>.Instance)
            .SinConsultaAsync(new ContextoCelda(CeldaEnCurso.De(celda), db), ct);
        (cierre.Estado, cierre.MotivoNa).Should().Be((EstadoCelda.Na, MotivoNa.DepartamentoNoSoportado));
    }

    // Plan C3 (:688-692): el primero crudo sin documento no entra a personas_vehiculo, así que el de orden 1 es el segundo crudo.
    [Fact]
    public async Task El_nombre_y_el_documento_son_de_la_misma_persona_si_el_primero_crudo_no_trae_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, json: """
            {"vehiculo":{"propietarios_actuales":[
              {"nombre_completo":"SIN DOCUMENTO DE PRUEBA"},
              {"id_tipo_documento":"C","no_documento":"9999900001","nombre_completo":"PRIMERO DE PRUEBA"}]}}
            """);

        var solicitud = await UnaAsync(api, sembrado, ct);

        solicitud.Documento.Numero.Should().Be(Documento);
        solicitud.PropietarioNombre.Should().Be(Nombre);
    }

    [Fact]
    public async Task El_propietario_que_corresponde_sin_nombre_cierra_sin_solicitud_y_no_usa_el_de_otra_persona()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, json: """
            {"vehiculo":{"propietarios_actuales":[
              {"nombre_completo":"SIN DOCUMENTO DE PRUEBA"},
              {"id_tipo_documento":"C","no_documento":"9999900001"}]}}
            """);

        await SinSolicitudPorNombreAsync(api, sembrado, ct);
    }

    // Dos filas del mismo documento (personas_vehiculo las une) con nombres distintos no identifican a nadie de forma segura.
    [Fact]
    public async Task Dos_nombres_distintos_para_el_documento_cierran_sin_solicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, json: """
            {"vehiculo":{"propietarios_actuales":[
              {"id_tipo_documento":"C","no_documento":"9999900001","nombre_completo":"UNA PERSONA DE PRUEBA"},
              {"tipo_documento":"Cédula Ciudadanía","no_documento":"9999900001","nombre_completo":"OTRA PERSONA DE PRUEBA"}]}}
            """);

        await SinSolicitudPorNombreAsync(api, sembrado, ct);
    }

    [Fact]
    public async Task El_RUNT_sin_respuesta_cifrada_cierra_sin_solicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var sembrado = await SembrarAsync(api, ct, sinRespuesta: true);

        await SinSolicitudPorNombreAsync(api, sembrado, ct);
    }

    // Plan C3 (:692): la reutilización del mismo día copia la respuesta cifrada; el impuesto del lote nuevo arma su solicitud con ella.
    [Fact]
    public async Task El_RUNT_reutilizado_el_mismo_dia_copia_la_cifrada_y_el_impuesto_arma_su_solicitud()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorNa.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var primero = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);
        await Motor.Bus(api).ProcesarLoteAsync(ct);
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote reutilizado", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"])), ct);
        creada.IsSuccessStatusCode.Should().BeTrue(await creada.Content.ReadAsStringAsync(ct));
        var segundo = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        (await Lotes.ConfirmarAsync(cliente, segundo, [Lotes.Fila("TST001")], ct)).IsSuccessStatusCode.Should().BeTrue();
        await Motor.Bus(api).ProcesarLoteAsync(ct);
        await Motor.EjecutarAsync(api, """
            INSERT INTO diagnostico.organismos (codigo, nombre, nombre_norm, departamento_codigo, departamento_nombre, municipio)
            VALUES ('98096000', 'SECRETARIA DE TRANSITO DE PRUEBA', 'SECRETARIA DE TRANSITO DE PRUEBA', '96', 'DEPARTAMENTO DE PRUEBA', 'MUNICIPIO DE PRUEBA')
            ON CONFLICT (codigo) DO NOTHING
            """, ct);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.vehiculos SET departamento_codigo = '{MotorNa.DepartamentoSoportadoFicticio}' WHERE diagnostico_id = '{segundo}'", ct);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, segundo, ct);
        var runtCopia = celdas.Single(c => c.Fuente == Fuente.Runt);
        var runtOrigen = (await Motor.CeldasAsync(api, empresa.Id, primero, ct)).Single(c => c.Fuente == Fuente.Runt);
        runtCopia.ReutilizadaDeId.Should().Be(runtOrigen.Id);
        runtCopia.RespuestaFuenteCifrada.Should().NotBeNullOrEmpty();
        var impuestoId = celdas.Single(x => x.Fuente == Fuente.ImpuestoDepartamental).Id;
        await using var db = empresa.Db();
        var impuesto = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == impuestoId, ct);

        var solicitud = (ConsultarImpuestoVehicularRequest)(await ConstruirAsync(api, db, impuesto, ct)).Should().ContainSingle().Subject.Solicitud;

        solicitud.Documento.Numero.Should().Be(Documento);
        solicitud.PropietarioNombre.Should().Be("PERSONA PRUEBA UNO");
    }

    private static async Task SinSolicitudPorNombreAsync(ApiDePrueba api, Sembrado sembrado, CancellationToken ct)
    {
        await using var db = sembrado.Empresa.Db();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == sembrado.ImpuestoId, ct);

        (await ConstruirAsync(api, db, celda, ct)).Should().BeEmpty("sin nombre seguro no se llama al proveedor ni se publica el trabajo");
        var cierre = await new EjecutorCeldaImpuesto(Docs(api), Respuestas(api), new ListaLog()).SinConsultaAsync(new ContextoCelda(CeldaEnCurso.De(celda), db), ct);
        (cierre.Estado, cierre.TipoError).Should().Be((EstadoCelda.Error, EjecutorCeldaImpuesto.SinNombrePropietario));
    }

    private static async Task<ConsultarImpuestoVehicularRequest> UnaAsync(ApiDePrueba api, Sembrado sembrado, CancellationToken ct)
    {
        await using var db = sembrado.Empresa.Db();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == sembrado.ImpuestoId, ct);
        return (ConsultarImpuestoVehicularRequest)(await ConstruirAsync(api, db, celda, ct)).Should().ContainSingle().Subject.Solicitud;
    }

    internal static async Task<Sembrado> SembrarAsync(
        ApiDePrueba api, CancellationToken ct, string tipo = "CC", string numero = Documento, string? json = null,
        bool placa = true, bool persona = true, bool modelo = true, bool municipio = true, string departamento = Departamento, bool soportar = true, bool sinRespuesta = false)
    {
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST901")], [Fuente.Runt, Fuente.ImpuestoDepartamental], null, null, ct);
        await using var db = empresa.Db();
        if (soportar)
            db.Add(new DepartamentoImpuesto { DepartamentoCodigo = departamento, Soportado = true, ActualizadoEn = DateTimeOffset.UtcNow });
        db.Add(new Organismo
        {
            Codigo = "98005000",
            Nombre = "SECRETARIA DE TRANSITO DE PRUEBA",
            NombreNorm = "SECRETARIA DE TRANSITO DE PRUEBA",
            DepartamentoCodigo = departamento,
            DepartamentoNombre = "DEPARTAMENTO DE PRUEBA",
            Municipio = municipio ? "MUNICIPIO DE PRUEBA" : null,
        });
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.DiagnosticoId == lote, ct);
        vehiculo.Placa = placa ? "TST901" : null;
        vehiculo.DepartamentoCodigo = departamento;
        vehiculo.OrganismoNombre = "SECRETARIA DE TRANSITO DE PRUEBA";
        vehiculo.DatosConsolidados = modelo ? """{"vehiculo":{"modelo":2020}}""" : "{}";
        var runt = await db.Set<CeldaConsulta>().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == Fuente.Runt, ct);
        runt.Estado = EstadoCelda.Completado;
        runt.RespuestaFuenteCifrada = sinRespuesta ? null : Respuestas(api).Proteger(json ?? JsonConDosNombres);
        if (persona)
            db.Add(Persona(api, empresa.Id, vehiculo.Id, tipo, numero));
        await db.SaveChangesAsync(ct);
        var impuesto = await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == Fuente.ImpuestoDepartamental, ct);
        return new Sembrado(empresa, lote, vehiculo.Id, impuesto.Id);
    }

    private static PersonaVehiculo Persona(ApiDePrueba api, Guid empresa, Guid vehiculo, string tipo, string numero) => new()
    {
        TenantId = empresa,
        VehiculoId = vehiculo,
        Rol = RolPersona.Propietario,
        Orden = 1,
        DocTipo = tipo,
        DocCifrado = Docs(api).Proteger(numero),
        Origen = OrigenDocumento.Proveedor,
        CreadoEn = DateTimeOffset.UtcNow,
    };

    private static string Json(string nombre, string tipo, string numero) =>
        "{\"vehiculo\":{\"propietarios_actuales\":[{\"id_tipo_documento\":\"" + tipo + "\",\"no_documento\":\"" + numero + "\",\"nombre_completo\":\"" + nombre + "\"}]}}";

    private static async Task<IReadOnlyList<SolicitudConsulta>> ConstruirAsync(ApiDePrueba api, DiagnosticoDb db, CeldaConsulta celda, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var constructor = scope.ServiceProvider.GetServices<IConstructorSolicitud>().Single(c => c.Fuente == Fuente.ImpuestoDepartamental);
        return await constructor.ConstruirAsync(db, celda, ct);
    }

    private static IProtectorDocumentoPersona Docs(ApiDePrueba api) => api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();

    private static IProtectorRespuestaFuente Respuestas(ApiDePrueba api) => api.App.Services.GetRequiredService<IProtectorRespuestaFuente>();

    internal sealed record Sembrado(EmpresaDePrueba Empresa, Guid LoteId, Guid VehiculoId, Guid ImpuestoId);

    private sealed class ListaLog : ILogger<EjecutorCeldaImpuesto>
    {
        public List<string> Textos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Textos.Add(formatter(state, exception));
    }
}
