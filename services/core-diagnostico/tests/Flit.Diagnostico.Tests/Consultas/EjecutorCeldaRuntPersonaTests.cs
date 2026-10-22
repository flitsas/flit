using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Configuracion;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13792 (F05-H3), AC1 y AC5-AC7 (F05-C4, F05-C9, F05-C10, F05-C11, F05-C14, decisión Q2) — celdas RUNT_PROPIETARIO y
/// RUNT_LOCATARIO (<see cref="EjecutorCeldaRuntPersona"/>): la persona natural va por <c>ConsultarConductor</c> y la de NIT por
/// <c>ConsultarRues</c> (sin dígito de verificación), con los adaptadores reales sobre un cliente gRPC falso
/// (<see cref="ConsultasGrpcFalso"/>) que anota la empresa de la metadata; el payload no guarda nombres, razón social ni
/// documentos; la reutilización del mismo día solo aplica si el RUNT del vehículo vino del mismo origen; y ni los logs ni los
/// intentos contienen un documento. PostgreSQL real; placas TST3## y documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorCeldaRuntPersonaTests
/// </code>
/// </example>
public sealed class EjecutorCeldaRuntPersonaTests
{
    private const string FuncionarioFicticio = "PERSONA PRUEBA UNO";
    private const string RazonSocialFicticia = "EMPRESA FICTICIA SAS";
    private const string RepresentanteFicticio = "PERSONA PRUEBA TRES";
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    // AC1 (F05-C4, F05-C10, Q2) — dos naturales por ConsultarConductor y el NIT por ConsultarRues sin dígito de verificación;
    // la celda del locatario consulta al locatario.
    [Fact]
    public async Task Natural_consulta_conductor_y_NIT_consulta_RUES()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ConsultasGrpcFalso();
        await using var api = await MotorPersonas.ArrancarAsync(null, b => grpc.RegistrarAdaptadoresReales(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301"), Lotes.Fila("TST303")], ct, conLocatario: true);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Where(l => l.Metodo == "ConsultarConductor").Select(l => (l.Tipo, l.Numero)).Should().BeEquivalentTo(
            [("CC", RuntConPersonas.Natural1), ("CE", RuntConPersonas.Natural2), ("CC", RuntConPersonas.Locatario)]);
        grpc.Llamadas.Where(l => l.Metodo == "ConsultarRues").Select(l => l.Numero).Should().BeEquivalentTo(
            [RuntConPersonas.Nit, RuntConPersonas.DuenoLeasing], "el NIT va sin dígito de verificación");
        (await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct)).Estado.Should().Be(EstadoCelda.Completado);
        var locatario = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST303", Fuente.RuntLocatario, ct);
        locatario.Estado.Should().Be(EstadoCelda.Completado);
        MotorPersonas.PersonasDelPayload(locatario.Payload).Should().ContainSingle().Which.GetProperty("consulta").GetString().Should().Be("RUNT_PERSONA");
    }

    // AC5 (F05-C9) — el RUES responde con razón social, NIT y representantes, y el RUNT persona con nombres: el payload trae solo
    // el estado de la matrícula y la última renovación, y del RUNT persona la inscripción y las licencias.
    [Fact]
    public async Task Payload_sin_nombres_razon_social_ni_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ConsultasGrpcFalso();
        await using var api = await MotorPersonas.ArrancarAsync(null, b => grpc.RegistrarAdaptadoresReales(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        celda.Payload.Should().NotContainAny(FuncionarioFicticio, RazonSocialFicticia, RepresentanteFicticio, "CALLE FICTICIA")
            .And.NotContainAny(RuntConPersonas.Natural1, RuntConPersonas.Natural2, RuntConPersonas.Nit);
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        var rues = personas.Single(p => p.GetProperty("consulta").GetString() == "RUES").GetProperty("resultado");
        rues.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["encontrada", "estado_matricula", "ultima_renovacion"]);
        rues.GetProperty("estado_matricula").GetString().Should().Be("ACTIVA");
        rues.GetProperty("ultima_renovacion").GetString().Should().Be("2026-03-15");
        var runt = personas.First(p => p.GetProperty("consulta").GetString() == "RUNT_PERSONA").GetProperty("resultado");
        runt.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["inscrito", "estado_conductor", "licencias"]);
        runt.GetProperty("inscrito").GetBoolean().Should().BeTrue();
        runt.GetProperty("licencias").EnumerateArray().Select(l => l.GetProperty("categoria").GetString()).Should().Equal("B1", "C1");
    }

    // AC6 (F05-C11, decisión 3) — mismo día: con el RUNT reutilizado del mismo origen (mismas personas) la celda de persona se
    // reutiliza sin llamar (un intento desde_cache por persona, con las personas del vehículo nuevo); con el RUNT consultado de
    // nuevo (por VIN) y otras personas, no reutiliza el dato del otro lote y consulta.
    [Fact]
    public async Task Reutiliza_solo_si_el_RUNT_vino_del_mismo_origen()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var fuentes = new FuentesPersonaProgramables();
        await using var api = await MotorPersonas.ArrancarAsync(fuentes, b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        IrA(reloj, 8, 0);
        var primero = await LoteRepetibleAsync(api, empresa, "TST301", ct);
        IrA(reloj, 9, 0);
        var segundo = await LoteRepetibleAsync(api, empresa, "TST301", ct);

        fuentes.Llamadas.Should().HaveCount(3, "el segundo lote reutiliza RUNT y RUNT_PROPIETARIO sin llamar");
        var original = await MotorPersonas.CeldaAsync(api, empresa, primero, "TST301", Fuente.RuntPropietario, ct);
        var copia = await MotorPersonas.CeldaAsync(api, empresa, segundo, "TST301", Fuente.RuntPropietario, ct);
        (copia.Estado, copia.ReutilizadaDeId, copia.DatoObtenidoEn).Should().Be((EstadoCelda.Completado, (Guid?)original.Id, original.DatoObtenidoEn));
        var personasCopia = await MotorPersonas.PersonasAsync(empresa, copia.VehiculoId, RolPersona.Propietario, ct);
        MotorPersonas.PersonasDelPayload(copia.Payload).Select(p => p.GetProperty("persona_id").GetGuid())
            .Should().Equal(personasCopia.Select(p => p.Id), "el payload apunta a las personas del vehículo nuevo");
        var intentosCopia = await MotorPersonas.IntentosAsync(empresa, copia.Id, ct);
        intentosCopia.Should().HaveCount(3).And.OnlyContain(i => i.DesdeCache && i.Servicio == null && i.Estado == ResultadoIntento.Success);
        intentosCopia.Select(i => i.PersonaId).Should().BeEquivalentTo(personasCopia.Select(p => (Guid?)p.Id));

        IrA(reloj, 10, 0);
        var tercero = await LoteRepetibleAsync(api, empresa, RuntConPersonas.VinTst301, ct);

        var nueva = await MotorPersonas.CeldaAsync(api, empresa, tercero, RuntConPersonas.VinTst301, Fuente.RuntPropietario, ct);
        nueva.ReutilizadaDeId.Should().BeNull("el RUNT se consultó de nuevo y trajo otras personas");
        nueva.Estado.Should().Be(EstadoCelda.Completado);
        fuentes.LlamadasA(RuntConPersonas.OtroNatural1).Should().Be(1);
        fuentes.LlamadasA(RuntConPersonas.OtroNatural2).Should().Be(1);
        fuentes.Llamadas.Should().HaveCount(5);
        (await MotorPersonas.IntentosAsync(empresa, nueva.Id, ct)).Should().HaveCount(2).And.OnlyContain(i => !i.DesdeCache);
    }

    // AC7 (F05-C14, fuga) — dos compañías con la misma placa: cada llamada lleva en la metadata la empresa de su celda y cada
    // compañía consulta a sus propias personas; ningún intento lleva un documento.
    [Fact]
    public async Task Llamada_lleva_la_empresa_de_la_celda()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ConsultasGrpcFalso();
        var runt = new RuntConPersonas();
        await using var api = await MotorPersonas.ArrancarAsync(null, b => grpc.RegistrarAdaptadoresReales(b), runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var dos = DosEmpresas.En(api);
        runt.PorEmpresa[(dos.A.Id, "TST304")] = [RuntConPersonas.Propio("CC", "9999900341"), RuntConPersonas.Propio("NIT", "999990345")];
        runt.PorEmpresa[(dos.B.Id, "TST304")] = [RuntConPersonas.Propio("CC", "9999900342")];
        var deA = await MotorPersonas.LoteAsync(api, dos.A, [Lotes.Fila("TST304")], ct);
        var deB = await MotorPersonas.LoteAsync(api, dos.B, [Lotes.Fila("TST304")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Select(l => (l.Numero, l.Tenant)).Should().BeEquivalentTo(
            [("9999900341", dos.A.Id.ToString()), ("999990345", dos.A.Id.ToString()), ("9999900342", dos.B.Id.ToString())]);
        var celdaA = await MotorPersonas.CeldaAsync(api, dos.A, deA, "TST304", Fuente.RuntPropietario, ct);
        var celdaB = await MotorPersonas.CeldaAsync(api, dos.B, deB, "TST304", Fuente.RuntPropietario, ct);
        celdaB.ReutilizadaDeId.Should().BeNull("nunca se reutiliza el dato de otra compañía");
        var intentosA = await MotorPersonas.IntentosAsync(dos.A, celdaA.Id, ct);
        intentosA.Should().HaveCount(2).And.OnlyContain(i => i.TenantId == dos.A.Id && i.IdentificadorConsultado == null);
        (await MotorPersonas.IntentosAsync(dos.B, celdaB.Id, ct)).Should().ContainSingle().Which.TenantId.Should().Be(dos.B.Id);
        (await MotorPersonas.IntentosAsync(dos.B, celdaA.Id, ct)).Should().BeEmpty("B no ve los intentos de A");
        (await FilasAsync(api, "SELECT row_to_json(i)::text FROM diagnostico.intentos_consulta i", ct))
            .Should().OnlyContain(f => !f.Contains("9999900341") && !f.Contains("999990345") && !f.Contains("9999900342"));
    }

    // AC7 (F05-C14, R-3) — con los adaptadores simulados (OK, sin licencias, RUES activa, Timeout e inválida) ningún log, intento
    // ni payload contiene un documento.
    [Fact]
    public async Task Ningun_log_ni_intento_contiene_el_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        var runt = new RuntConPersonas();
        await using var api = await MotorPersonas.ArrancarAsync(null, b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
            b.Services.AddScoped<IFuenteRuntPersona, FuenteRuntPersonaSimulada>();
            b.Services.AddScoped<IFuenteRues, FuenteRuesSimulada>();
        }, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        string[] conError = ["9999900307", "9999900318"];
        runt.PorEmpresa[(empresa.Id, "TST305")] = [.. conError.Select(d => RuntConPersonas.Propio("CC", d))];
        var lote = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila("TST301"), Lotes.Fila("TST305")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        var sanas = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST301", Fuente.RuntPropietario, ct);
        sanas.Estado.Should().Be(EstadoCelda.Completado);
        var resultados = MotorPersonas.PersonasDelPayload(sanas.Payload).Select(p => p.GetProperty("resultado")).ToList();
        resultados[1].GetProperty("licencias").GetArrayLength().Should().Be(0, "el simulado con documento terminado en 2 no tiene licencias");
        resultados[2].GetProperty("estado_matricula").GetString().Should().Be("ACTIVA", "el simulado del RUES con NIT terminado en 5 está activo");
        var fallidas = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST305", Fuente.RuntPropietario, ct);
        (fallidas.Estado, fallidas.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError), "TIMEOUT y RESPUESTA_INVALIDA");

        string[] documentos = [RuntConPersonas.Natural1, RuntConPersonas.Natural2, RuntConPersonas.Nit, .. conError];
        logs.Mensajes.Should().NotBeEmpty().And.OnlyContain(m => !documentos.Any(d => m.Contains(d, StringComparison.Ordinal)));
        var filas = (await FilasAsync(api, "SELECT row_to_json(i)::text FROM diagnostico.intentos_consulta i WHERE fuente = 'RUNT_PROPIETARIO'", ct))
            .Concat(await FilasAsync(api, "SELECT coalesce(payload::text, '') FROM diagnostico.consultas WHERE fuente = 'RUNT_PROPIETARIO'", ct)).ToList();
        filas.Should().HaveCount(7, "cinco intentos y dos celdas");
        filas.Should().OnlyContain(f => !documentos.Any(d => f.Contains(d, StringComparison.Ordinal)));
    }

    private static async Task<List<string>> FilasAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(sql, conexion);
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        var filas = new List<string>();
        while (await lector.ReadAsync(ct))
            filas.Add(lector.GetString(0));
        return filas;
    }

    /// <summary>Lote con RUNT y RUNT_PROPIETARIO procesado hasta vaciar la cola; la configuración se siembra solo la primera vez.</summary>
    private static async Task<Guid> LoteRepetibleAsync(ApiDePrueba api, EmpresaDePrueba empresa, string identificador, CancellationToken ct)
    {
        bool sembrada;
        await using (var db = empresa.Db())
            sembrada = await db.Set<Flit.Diagnostico.Api.Modulos.Configuracion.ConfiguracionEmpresa>().AsNoTracking().AnyAsync(ct);

        Guid id;
        if (!sembrada)
        {
            id = await MotorPersonas.LoteAsync(api, empresa, [Lotes.Fila(identificador)], ct);
        }
        else
        {
            var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
            var creada = await cliente.PostAsync(Cabeceras.Ruta,
                ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote repetido", "SIMPLE", null, ["RUNT"], ["RUNT_PROPIETARIO"])), ct);
            creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
            id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
            var confirmada = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila(identificador)], ct);
            confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        }

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        return id;
    }

    /// <summary>Avanza el reloj hasta la próxima hh:mm en hora Colombia (siempre hacia adelante).</summary>
    private static void IrA(RelojManual reloj, int hora, int minuto)
    {
        var ahora = reloj.GetUtcNow();
        var local = ahora.ToOffset(Colombia);
        var destino = new DateTimeOffset(local.Year, local.Month, local.Day, hora, minuto, 0, Colombia);
        if (destino <= ahora)
            destino = destino.AddDays(1);
        reloj.Avanzar(destino - ahora);
    }
}

/// <summary>
/// Cliente gRPC falso de core-consultas (HU #13792): responde <c>ConsultarConductor</c> y <c>ConsultarRues</c> con datos
/// inventados que SÍ traen nombres, razón social, NIT, representantes y dirección (lo que el adaptador debe descartar) y anota
/// el método, la empresa de la metadata <c>x-flit-tenant-id</c> y el documento pedido.
/// </summary>
public sealed class ConsultasGrpcFalso : ConsultasService.ConsultasServiceClient
{
    private readonly ConcurrentQueue<(string Metodo, string? Tenant, string Tipo, string Numero)> _llamadas = new();

    public IReadOnlyCollection<(string Metodo, string? Tenant, string Tipo, string Numero)> Llamadas => _llamadas;

    /// <summary>Registra los adaptadores reales de RUNT persona y RUES sobre este cliente (antes que Program: ganan al TryAdd).</summary>
    public void RegistrarAdaptadoresReales(WebApplicationBuilder b)
    {
        b.Services.AddSingleton(new ClienteConsultas(this, NullLogger<ClienteConsultas>.Instance));
        b.Services.AddScoped<IFuenteRuntPersona, FuenteRuntPersonaReal>();
        b.Services.AddScoped<IFuenteRues, FuenteRuesReal>();
    }

    public override AsyncUnaryCall<ConsultarConductorResponse> ConsultarConductorAsync(ConsultarConductorRequest request, CallOptions options)
    {
        _llamadas.Enqueue(("ConsultarConductor", Tenant(options), request.Documento.Tipo, request.Documento.Numero));
        var resultado = new ResultadoConsulta { Proveedor = "kyverum_runt_conductor", Semaforo = Semaforo.Verde };
        resultado.Chequeos.Add(new Chequeo { Clave = "conductor_identidad", Etiqueta = "Persona en RUNT", Estado = EstadoChequeo.Ok, Fuente = "RUNT" });
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = "PERSONA PRUEBA UNO" });
        resultado.Campos.Add(new Campo { Clave = "person_license_status", ValorTexto = "ACTIVO" });
        resultado.Campos.Add(new Campo { Clave = "person_has_active_license", ValorTexto = "true" });
        resultado.Campos.Add(new Campo { Clave = "person_license_categories", ValorTexto = "B1,C1" });
        return Llamada(new ConsultarConductorResponse { Resultado = resultado });
    }

    public override AsyncUnaryCall<ConsultarRuesResponse> ConsultarRuesAsync(ConsultarRuesRequest request, CallOptions options)
    {
        _llamadas.Enqueue(("ConsultarRues", Tenant(options), "NIT", request.Nit));
        var resultado = new ResultadoConsulta { Proveedor = "verifik_rues", Semaforo = Semaforo.Verde, Certificaciones = new Certificaciones() };
        resultado.Campos.Add(new Campo { Clave = "rues_razon_social", ValorTexto = "EMPRESA FICTICIA SAS" });
        resultado.Campos.Add(new Campo { Clave = "rues_nit", ValorTexto = request.Nit });
        resultado.Campos.Add(new Campo { Clave = "rues_direccion", ValorTexto = "CALLE FICTICIA 1" });
        resultado.Campos.Add(new Campo { Clave = "rues_estado", ValorTexto = "ACTIVA" });
        var matricula = new MatriculaMercantil
        {
            Nit = new ValorCertificado { Valor = request.Nit },
            RazonSocial = new ValorCertificado { Valor = "EMPRESA FICTICIA SAS" },
            Estado = new EstadoVigencia { Valor = Vigencia.Vigente, Crudo = "ACTIVA" },
            RenovadaEn = new FechaCertificada { Valor = "2026-03-15" },
            Direccion = new ValorCertificado { Valor = "CALLE FICTICIA 1" },
        };
        matricula.RepresentantesLegales.Add(new RepresentanteLegal { Nombre = new ValorCertificado { Valor = "PERSONA PRUEBA TRES" } });
        resultado.Certificaciones.MatriculasMercantiles.Add(matricula);
        return Llamada(new ConsultarRuesResponse { Resultado = resultado });
    }

    private static string? Tenant(CallOptions options) => options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata);

    private static AsyncUnaryCall<T> Llamada<T>(T respuesta) =>
        new(Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
}
