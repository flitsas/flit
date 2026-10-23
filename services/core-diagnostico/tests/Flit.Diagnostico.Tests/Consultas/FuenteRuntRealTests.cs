using System.Collections.Concurrent;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using TipoErrorDiagnostico = Flit.Diagnostico.Api.Modulos.Consultas.Fuentes.TipoErrorFuente;
using TipoErrorIntempo = Flit.Consultas.Grpc.V1.TipoErrorFuente;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13878 (F10-H6, F10-C9, F10-C10) — <see cref="FuenteRuntReal"/>: la celda RUNT consulta <c>RuntIntempoService</c> de
/// core-consultas por <see cref="ClienteRuntIntempo"/> (segundo cliente gRPC, G1=a), con <c>ConsultarPorPlaca</c> o
/// <c>ConsultarPorVin</c> según <see cref="IdentificadorVehiculo.EsVin"/> (G2=b), la empresa de la celda en
/// <c>x-flit-tenant-id</c> y la respuesta por <see cref="MapperIntempo.Mapear(ConsultarVehiculoIntempoResponse, string)"/>.
/// FailedPrecondition y Unimplemented son <c>NO_CONECTADO</c> definitivo; Unavailable, DeadlineExceeded y un ERROR HTTP 503
/// dentro del mensaje son transitorios. core-consultas es un servidor gRPC falso sobre TestServer
/// (<see cref="ServidorRuntFalso"/>); nunca core-consultas ni Intempo reales. Las clases de celda usan PostgreSQL real.
/// Fixtures inventados: placa TST601, VIN 9ZZPRUEBA00000601, NIT 999990601.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FuenteRuntRealTests
/// </code>
/// </example>
public sealed class FuenteRuntRealTests
{
    internal const string Placa = "TST601";
    internal const string Vin = "9ZZPRUEBA00000601";
    internal const string Documento = "999990601";

    // AC1 (F10-C9) — por placa: ConsultarPorPlaca(placa) con la empresa en x-flit-tenant-id, ninguna llamada por VIN, y el
    // vehículo mapeado con el servicio INTEMPO y el propietario NIT (solo tipo y número).
    [Fact]
    public async Task Por_placa_llama_ConsultarPorPlaca_con_tenant_y_mapea_el_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso(), ct);
        var empresa = Guid.NewGuid();

        var resultado = await servidor.Fuente().ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, Placa), ct);

        servidor.Falso.Llamadas.Should().Equal(new LlamadaRunt("ConsultarPorPlaca", Placa, empresa.ToString()));
        resultado.Exitoso.Should().BeTrue(resultado.Mensaje);
        var vehiculo = resultado.Vehiculo!;
        (vehiculo.Placa, vehiculo.Vin, vehiculo.Servicio, vehiculo.ServicioQueResolvio, vehiculo.IdTransaccion)
            .Should().Be((Placa, Vin, "PARTICULAR", FuenteRuntReal.Servicio, (string?)null));
        vehiculo.Propietarios.Should().ContainSingle().Which.Should().BeEquivalentTo(new PropietarioRunt("NIT", Documento, TipoPropiedad.Propio, []));

        var sinEmpresa = () => servidor.Fuente().ConsultarVehiculoAsync(Guid.Empty, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, Placa), ct);
        await sinEmpresa.Should().ThrowAsync<ArgumentException>("la consulta exige la empresa de la celda");
        servidor.Falso.Llamadas.Should().HaveCount(1, "sin empresa no sale ninguna llamada");
    }

    // AC1 (F10-C9, G2=b) — por VIN: ConsultarPorVin(vin) y nunca ConsultarPorPlaca; la placa definitiva es la del RUNT.
    [Fact]
    public async Task Por_VIN_llama_ConsultarPorVin_y_nunca_ConsultarPorPlaca()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso(), ct);
        var empresa = Guid.NewGuid();

        var resultado = await servidor.Fuente().ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(TipoIdentificador.Vin, Vin), ct);

        servidor.Falso.Llamadas.Should().Equal(new LlamadaRunt("ConsultarPorVin", Vin, empresa.ToString()));
        resultado.Vehiculo!.Placa.Should().Be(Placa, "la placa definitiva es la del RUNT");
        resultado.Vehiculo.Vin.Should().Be(Vin);
    }

    // AC2 (F10-C10) — FailedPrecondition (Intempo sin configurar en core-consultas) y Unimplemented (core-consultas sin F10-H4):
    // NO_CONECTADO definitivo en la fuente y, en la celda, ERROR NO_CONECTADO tras un único intento aunque MaxReintentos=3.
    [Theory]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Unimplemented)]
    public async Task FailedPrecondition_y_Unimplemented_son_NO_CONECTADO_definitivo(StatusCode codigo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso { Falla = codigo }, ct);

        var directo = await servidor.Fuente().ConsultarVehiculoAsync(Guid.NewGuid(), new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, Placa), ct);
        (directo.Error, directo.EsTransitorio).Should().Be(((TipoErrorDiagnostico?)TipoErrorDiagnostico.NoConectado, false), codigo.ToString());

        await using var api = await ApiDePrueba.ConBaseAsync(configurar: servidor.RegistrarAdaptadorReal);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(Placa)], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 3), ct);

        servidor.Falso.Llamadas.Should().HaveCount(2, "una llamada directa y un solo intento de la celda: NO_CONECTADO no se reintenta");
        var celda = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.Runt);
        (celda.Estado, celda.TipoError, celda.IntentosAuto).Should().Be((EstadoCelda.Error, "NO_CONECTADO", 0));
        var intento = (await ReintentosTests.IntentosAsync(api, empresa, id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.TipoError).Should().Be((ResultadoIntento.Error, "NO_CONECTADO"));
    }

    // AC2 (F10-C10) — Unavailable (RED), DeadlineExceeded (TIMEOUT) y un ERROR HTTP_NO_EXITOSO 503 dentro del mensaje (HTTP_5XX)
    // son transitorios: con MaxReintentos=1 hay dos intentos con ese tipo, el segundo tras el backoff, y la celda queda en ERROR.
    [Theory]
    [InlineData("Unavailable", "RED")]
    [InlineData("DeadlineExceeded", "TIMEOUT")]
    [InlineData("HTTP_503", "HTTP_5XX")]
    public async Task Unavailable_y_DeadlineExceeded_son_transitorios(string caso, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var falso = caso == "HTTP_503"
            ? new RuntIntempoFalso { Error503 = true }
            : new RuntIntempoFalso { Falla = Enum.Parse<StatusCode>(caso) };
        await using var servidor = await ServidorRuntFalso.IniciarAsync(falso, ct);
        var directo = await servidor.Fuente().ConsultarVehiculoAsync(Guid.NewGuid(), new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, Placa), ct);
        directo.EsTransitorio.Should().BeTrue(caso);
        TipoErrorFuenteCodigos.Codigo(directo.Error!.Value).Should().Be(tipo);
        falso.Reiniciar();

        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            servidor.RegistrarAdaptadorReal(b);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(Placa)], ct);
        var procesador = Motor.Procesador(api, maxReintentos: 1);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);
        var enEspera = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.Runt);
        (enEspera.Estado, enEspera.IntentosAuto).Should().Be((EstadoCelda.Pendiente, 1), "queda el reintento");
        reloj.Avanzar(PoliticaReintentos.Espera(1));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        falso.Llamadas.Should().HaveCount(2).And.OnlyContain(l => l.Metodo == "ConsultarPorPlaca");
        var celda = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.Runt);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Error, tipo));
        (await ReintentosTests.IntentosAsync(api, empresa, id, ct)).Select(i => (i.Estado, i.TipoError))
            .Should().Equal((ResultadoIntento.Error, tipo), (ResultadoIntento.Error, tipo));
    }

    // Contrato de la HU (Errores gRPC): Unauthenticated y PermissionDenied → AUTENTICACION; InvalidArgument y el resto →
    // ESTADO_NO_OK; todos definitivos. El mensaje es fijo: nunca copia el detalle del servidor.
    [Theory]
    [InlineData(StatusCode.Unauthenticated, "AUTENTICACION")]
    [InlineData(StatusCode.PermissionDenied, "AUTENTICACION")]
    [InlineData(StatusCode.InvalidArgument, "ESTADO_NO_OK")]
    [InlineData(StatusCode.Internal, "ESTADO_NO_OK")]
    public async Task Autenticacion_y_demas_codigos_son_definitivos(StatusCode codigo, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso { Falla = codigo }, ct);

        var resultado = await servidor.Fuente().ConsultarVehiculoAsync(Guid.NewGuid(), new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, Placa), ct);

        TipoErrorFuenteCodigos.Codigo(resultado.Error!.Value).Should().Be(tipo);
        resultado.EsTransitorio.Should().BeFalse();
        resultado.Mensaje.Should().NotContain(Placa).And.NotContain(Documento).And.NotContain(RuntIntempoFalso.Detalle);
    }

    // AC1 (F10-C9) — la celda RUNT de TST601 para la empresa E: el servidor recibe ConsultarPorPlaca(TST601) con
    // x-flit-tenant-id=E y ninguna por VIN; la celda queda COMPLETADO con servicio INTEMPO y el documento del propietario cifrado.
    [Fact]
    public async Task La_celda_RUNT_completa_con_la_fuente_real_y_cifra_los_documentos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso(), ct);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: servidor.RegistrarAdaptadorReal);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(Placa)], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        servidor.Falso.Llamadas.Should().Equal(new LlamadaRunt("ConsultarPorPlaca", Placa, empresa.Id.ToString()));
        var celda = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single(c => c.Fuente == Fuente.Runt);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        var intento = (await ReintentosTests.IntentosAsync(api, empresa, id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.Servicio).Should().Be((ResultadoIntento.Success, (ServicioConsulta?)ServicioConsulta.Intempo));
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        (vehiculo.Placa, vehiculo.Vin, vehiculo.Servicio).Should().Be((Placa, Vin, "PARTICULAR"));
        vehiculo.PropietarioDocTipo.Should().Be("NIT");
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Desproteger(vehiculo.PropietarioDocCifrado!).Should().Be(Documento);

        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT row_to_json(v)::text || coalesce(c.payload::text, '') FROM diagnostico.vehiculos v " +
            "JOIN diagnostico.consultas c ON c.vehiculo_id = v.id AND c.fuente = 'RUNT' WHERE v.id = @id", conexion);
        consulta.Parameters.AddWithValue("id", vehiculo.Id);
        var fila = (string)(await consulta.ExecuteScalarAsync(ct))!;
        fila.Should().NotContain(Documento, "el documento solo se guarda cifrado").And.NotContain(RuntIntempoFalso.Nombre, "nunca nombres");
    }

    // Seguridad (PII) — una celda que completa y otra cuyo servidor falla con la placa y el documento en el detalle: ningún log
    // lleva el documento ni el detalle del servidor, y los del cliente, la fuente y el ejecutor tampoco la placa ni el VIN.
    [Fact]
    public async Task Ningun_log_lleva_placa_ni_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        const string PlacaConFalla = "TST602";
        var logs = new LogsEnMemoria();
        await using var servidor = await ServidorRuntFalso.IniciarAsync(new RuntIntempoFalso { FallaPara = { [PlacaConFalla] = StatusCode.Unavailable } }, ct);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
            servidor.RegistrarAdaptadorReal(b);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(Placa), Lotes.Fila(PlacaConFalla)], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Where(c => c.Fuente == Fuente.Runt).Select(c => c.Estado).Should().BeEquivalentTo([EstadoCelda.Completado, EstadoCelda.Error]);
        logs.Mensajes.Should().NotContain(m => m.Contains(Documento, StringComparison.Ordinal) || m.Contains(RuntIntempoFalso.Detalle, StringComparison.Ordinal)
            || m.Contains(RuntIntempoFalso.Nombre, StringComparison.Ordinal));
        var propios = logs.Mensajes.Where(m => m.Contains(nameof(ClienteRuntIntempo), StringComparison.Ordinal)
            || m.Contains(nameof(FuenteRuntReal), StringComparison.Ordinal) || m.Contains("EjecutorCeldaRunt ", StringComparison.Ordinal)).ToList();
        propios.Should().Contain(m => m.Contains(nameof(ClienteRuntIntempo), StringComparison.Ordinal) && m.Contains("Unavailable", StringComparison.Ordinal),
            "el fallo se registra con el método y el código");
        propios.Should().NotContain(m => m.Contains(Placa, StringComparison.Ordinal) || m.Contains(PlacaConFalla, StringComparison.Ordinal) || m.Contains(Vin, StringComparison.Ordinal));
    }
}

/// <summary>Una llamada que recibió el servidor falso: rpc, placa o VIN y la empresa de <c>x-flit-tenant-id</c>.</summary>
internal sealed record LlamadaRunt(string Metodo, string Valor, string? Tenant);

/// <summary>
/// core-consultas falso (HU #13878): <c>RuntIntempoService</c> que anota cada llamada y responde ENCONTRADO con el vehículo
/// inventado TST601 (NIT 999990601, propio), un ERROR HTTP_NO_EXITOSO 503 dentro del mensaje (<see cref="Error503"/>) o lanza
/// <see cref="Falla"/> (o la de <see cref="FallaPara"/> para un valor) con la placa y el documento en el detalle, para comprobar
/// que nunca se copian.
/// </summary>
internal sealed class RuntIntempoFalso : RuntIntempoService.RuntIntempoServiceBase
{
    public const string Detalle = "detalle-del-servidor-que-no-debe-salir";
    public const string Nombre = "EMPRESA PRUEBA SEIS SAS";

    private readonly ConcurrentQueue<LlamadaRunt> _llamadas = new();

    public StatusCode? Falla { get; init; }

    public Dictionary<string, StatusCode> FallaPara { get; } = new(StringComparer.Ordinal);

    public bool Error503 { get; init; }

    public IReadOnlyCollection<LlamadaRunt> Llamadas => _llamadas;

    /// <summary>Autorización y deadline de la última llamada (para la prueba del registro).</summary>
    public string? Autorizacion { get; private set; }

    public DateTime? Deadline { get; private set; }

    public void Reiniciar() => _llamadas.Clear();

    public override Task<ConsultarVehiculoIntempoResponse> ConsultarPorPlaca(ConsultarPorPlacaRequest request, ServerCallContext context) =>
        Responder("ConsultarPorPlaca", request.Placa, context);

    public override Task<ConsultarVehiculoIntempoResponse> ConsultarPorVin(ConsultarPorVinRequest request, ServerCallContext context) =>
        Responder("ConsultarPorVin", request.Vin, context);

    private Task<ConsultarVehiculoIntempoResponse> Responder(string metodo, string valor, ServerCallContext context)
    {
        _llamadas.Enqueue(new LlamadaRunt(metodo, valor, context.RequestHeaders.GetValue(PlatformServiceCallInterceptor.TenantMetadata)));
        Autorizacion = context.RequestHeaders.GetValue("authorization");
        Deadline = context.Deadline == DateTime.MaxValue ? null : context.Deadline;
        var falla = FallaPara.TryGetValue(valor, out var propia) ? propia : Falla;
        if (falla is { } codigo)
            throw new RpcException(new Status(codigo, $"{Detalle} {valor} {FuenteRuntRealTests.Documento}"));

        if (Error503)
        {
            return Task.FromResult(new ConsultarVehiculoIntempoResponse
            {
                Proveedor = "intempo",
                Resultado = ResultadoRuntIntempo.Error,
                TipoError = TipoErrorIntempo.HttpNoExitoso,
                HttpStatus = 503,
                MensajeError = $"Intempo respondió 503: {Detalle}",
            });
        }

        return Task.FromResult(JsonParserIntempo.Parse($$"""
            {
              "proveedor": "intempo",
              "resultado": "RESULTADO_RUNT_INTEMPO_ENCONTRADO",
              "httpStatus": 200,
              "vehiculo": {
                "plate": "{{FuenteRuntRealTests.Placa}}",
                "informacionGeneral": {
                  "noPlaca": "{{FuenteRuntRealTests.Placa}}",
                  "noVin": "{{FuenteRuntRealTests.Vin}}",
                  "tipoServicio": "PARTICULAR",
                  "claseVehiculo": "AUTOMOVIL",
                  "organismoTransito": "SECRETARIA DE TRANSITO DE PRUEBA"
                },
                "propietariosActuales": [
                  {
                    "idTipoDocumento": "N",
                    "tipoDocumento": "NIT",
                    "noDocumento": "{{FuenteRuntRealTests.Documento}}",
                    "nombreCompleto": "{{Nombre}}",
                    "tipoPropiedad": 1,
                    "detallePropiedad": "PROPIO"
                  }
                ]
              }
            }
            """));
    }
}

/// <summary>
/// Servidor gRPC falso de core-consultas sobre TestServer (patrón de <c>ContratoImpuestoProtoTests</c>) con
/// <see cref="RuntIntempoFalso"/>: entrega el cliente generado sobre su canal, el handler para el cliente de la fábrica y el
/// registro del adaptador real en una API de prueba.
/// </summary>
internal sealed class ServidorRuntFalso : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly GrpcChannel _canal;

    private ServidorRuntFalso(WebApplication app, RuntIntempoFalso falso)
    {
        _app = app;
        Falso = falso;
        _canal = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
    }

    public RuntIntempoFalso Falso { get; }

    public RuntIntempoService.RuntIntempoServiceClient Cliente => new(_canal);

    public static async Task<ServidorRuntFalso> IniciarAsync(RuntIntempoFalso falso, CancellationToken ct)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Services.AddGrpc();
        b.Services.AddSingleton(falso);
        var app = b.Build();
        app.MapGrpcService<RuntIntempoFalso>();
        await app.StartAsync(ct);
        return new ServidorRuntFalso(app, falso);
    }

    /// <summary>Handler hacia el servidor, para <c>ConfigurePrimaryHttpMessageHandler</c> del cliente de la fábrica.</summary>
    public HttpMessageHandler Handler() => _app.GetTestServer().CreateHandler();

    public FuenteRuntReal Fuente(ILogger<ClienteRuntIntempo>? log = null) =>
        new(new ClienteRuntIntempo(Cliente, log ?? NullLogger<ClienteRuntIntempo>.Instance));

    /// <summary>El adaptador real del RUNT sobre este servidor (antes que Program: gana al TryAdd).</summary>
    public void RegistrarAdaptadorReal(WebApplicationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Services.AddSingleton(sp => new ClienteRuntIntempo(Cliente, sp.GetRequiredService<ILogger<ClienteRuntIntempo>>()));
        b.Services.AddScoped<IFuenteRunt, FuenteRuntReal>();
    }

    public async ValueTask DisposeAsync()
    {
        _canal.Dispose();
        await _app.DisposeAsync();
    }
}
