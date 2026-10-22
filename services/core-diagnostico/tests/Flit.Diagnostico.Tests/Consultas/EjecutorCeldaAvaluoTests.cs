using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13795 (F05-H6), AC1-AC4 (F05-C1, F05-C7, F05-C8) — celda AVALUO (<see cref="EjecutorCeldaAvaluo"/>): consulta
/// <see cref="IFuenteAvaluo"/> con la placa y el VIN del RUNT y los datos técnicos que el RUNT dejó en
/// <c>datos_consolidados.vehiculo</c> (modelo, cilindraje, combustible y pasajeros); con valor completa con el valor sugerido y
/// los valores por fuente; sin valor en ninguna fuente completa con <c>sin_valor_comercial=true</c> (decisión Q4 = b), que no
/// es ERROR y por eso no se reprocesa (409); un Timeout agotado deja AVALUO en ERROR sin bloquear el vehículo. PostgreSQL real;
/// placas TST2## y VIN 9ZZPRUEBA######## del rango ficticio.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorCeldaAvaluoTests
/// </code>
/// </example>
public sealed class EjecutorCeldaAvaluoTests
{
    // AC1 (F05-C7, F05-C1) — TST201 con RUNT COMPLETADO (fixture persona natural: modelo 2020, cilindraje 1598, GASOLINA, 5
    // pasajeros): la llamada gRPC a core-consultas lleva placa, VIN y esos datos técnicos, con la empresa en la metadata; el
    // vehículo guarda los datos técnicos en datos_consolidados.vehiculo y la celda completa con el valor de la fuente principal.
    [Fact]
    public async Task Envia_modelo_cilindraje_combustible_y_pasajeros_del_RUNT()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new AvaluosGrpcFalso();
        await using var api = await MotorRunt.ArrancarAsync(grpc.RegistrarAdaptadorReal);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST201")], ct, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var llamada = grpc.Llamadas.Should().ContainSingle().Subject;
        llamada.Tenant.Should().Be(empresa.Id.ToString());
        (llamada.Request.Placa?.Valor, llamada.Request.Vin?.Valor).Should().Be(("TST201", "9ZZPRUEBA00000001"));
        (llamada.Request.Modelo, llamada.Request.Cilindraje, llamada.Request.Combustible, llamada.Request.Pasajeros)
            .Should().Be((2020, 1598, "GASOLINA", 5));
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST201", ct);
        using (var consolidado = JsonDocument.Parse(vehiculo.DatosConsolidados))
        {
            var v = consolidado.RootElement.GetProperty("vehiculo");
            (v.GetProperty("marca").GetString(), v.GetProperty("linea").GetString()).Should().Be(("MARCA PRUEBA", "LINEA PRUEBA"));
            (v.GetProperty("modelo").GetInt32(), v.GetProperty("cilindraje").GetInt32(), v.GetProperty("combustible").GetString(), v.GetProperty("pasajeros").GetInt32())
                .Should().Be((2020, 1598, "GASOLINA", 5));
        }

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST201", Fuente.Avaluo, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        using var payload = JsonDocument.Parse(celda.Payload!);
        var raiz = payload.RootElement;
        (raiz.GetProperty("sin_valor_comercial").GetBoolean(), raiz.GetProperty("valor_sugerido").GetInt64(), raiz.GetProperty("fuente_principal").GetString())
            .Should().Be((false, 48_500_000L, "fasecolda"));
        var fuentes = raiz.GetProperty("fuentes").EnumerateArray().ToList();
        fuentes.Select(f => (f.GetProperty("fuente").GetString(), f.GetProperty("estado").GetString()))
            .Should().Equal(("fasecolda", "OK"), ("mercado_libre", "OK"), ("base_gravable", "SIN_DATOS"));
        fuentes[1].GetProperty("muestras").GetInt32().Should().Be(12);
        fuentes[2].TryGetProperty("valor", out _).Should().BeFalse("sin datos no hay valor");
        celda.Payload.Should().NotContain("MENSAJE DEL PROVEEDOR", "el mensaje del proveedor nunca se guarda");
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.IdentificadorConsultado, intento.Servicio).Should().Be((ResultadoIntento.Success, "TST201", (ServicioConsulta?)ServicioConsulta.Fasecolda));
    }

    // AC1 (F05-C7) — simulación: TST201 trae las tres fuentes con valor y TST202 solo Fasecolda; cada fuente lleva su fecha (la
    // del dato, hora Colombia) y el valor sugerido sale de la fuente principal.
    [Fact]
    public async Task Completa_con_valor_sugerido_y_valores_por_fuente()
    {
        var ct = TestContext.Current.CancellationToken;
        var avaluo = new AvaluoEspia(new FuenteAvaluoSimulada());
        await using var api = await MotorRunt.ArrancarAsync(b => avaluo.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST201"), Lotes.Fila("TST202")], ct, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var tres = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST201", Fuente.Avaluo, ct);
        tres.Estado.Should().Be(EstadoCelda.Completado);
        var fecha = DateOnly.FromDateTime(tres.DatoObtenidoEn!.Value.ToOffset(TimeSpan.FromHours(-5)).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using (var payload = JsonDocument.Parse(tres.Payload!))
        {
            var raiz = payload.RootElement;
            raiz.GetProperty("sin_valor_comercial").GetBoolean().Should().BeFalse();
            raiz.GetProperty("valor_sugerido").GetInt64().Should().BePositive();
            raiz.GetProperty("fuente_principal").GetString().Should().Be("fasecolda");
            var fuentes = raiz.GetProperty("fuentes").EnumerateArray().ToList();
            fuentes.Select(f => f.GetProperty("fuente").GetString()).Should().Equal("fasecolda", "mercado_libre", "base_gravable");
            fuentes.Should().OnlyContain(f => f.GetProperty("estado").GetString() == "OK" && f.GetProperty("valor").GetInt64() > 0 && f.GetProperty("fecha").GetString() == fecha);
            fuentes[0].GetProperty("valor").GetInt64().Should().Be(raiz.GetProperty("valor_sugerido").GetInt64(), "el sugerido es el de la fuente principal");
        }

        var soloFasecolda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST202", Fuente.Avaluo, ct);
        soloFasecolda.Estado.Should().Be(EstadoCelda.Completado);
        using (var payload = JsonDocument.Parse(soloFasecolda.Payload!))
        {
            var fuentes = payload.RootElement.GetProperty("fuentes").EnumerateArray().ToList();
            fuentes.Where(f => f.GetProperty("estado").GetString() == "OK").Select(f => f.GetProperty("fuente").GetString()).Should().Equal("fasecolda");
            fuentes.Where(f => f.GetProperty("fuente").GetString() != "fasecolda").Should().OnlyContain(f => f.GetProperty("estado").GetString() == "SIN_DATOS");
        }

        avaluo.Llamadas.Select(l => l.Solicitud.Placa).Should().BeEquivalentTo(["TST201", "TST202"]);
    }

    // AC2 (F05-C7) — TST204 sin valor en ninguna fuente: COMPLETADO con sin_valor_comercial=true (no ERROR, Q4 = b), sin valor
    // sugerido ni fuente principal y con un intento exitoso.
    [Fact]
    public async Task Sin_valor_completa_con_sin_valor_comercial()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddScoped<IFuenteAvaluo, FuenteAvaluoSimulada>());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST204")], ct, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST204", Fuente.Avaluo, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Completado, (string?)null));
        celda.DatoObtenidoEn.Should().NotBeNull();
        using var payload = JsonDocument.Parse(celda.Payload!);
        var raiz = payload.RootElement;
        raiz.GetProperty("sin_valor_comercial").GetBoolean().Should().BeTrue();
        raiz.TryGetProperty("valor_sugerido", out _).Should().BeFalse("sin valor no hay sugerido");
        raiz.TryGetProperty("fuente_principal", out _).Should().BeFalse();
        raiz.GetProperty("fuentes").EnumerateArray().Should().OnlyContain(f => f.GetProperty("estado").GetString() == "SIN_DATOS");
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Which.Estado.Should().Be(ResultadoIntento.Success);
    }

    // AC3 (negativo, F05-C7) — esa celda COMPLETADO sin valor y un admin de la compañía: el reproceso responde 409
    // CELDA_NO_EN_ERROR, la celda no cambia y no se vuelve a llamar a la fuente.
    [Fact]
    public async Task Sin_valor_no_admite_reproceso()
    {
        var ct = TestContext.Current.CancellationToken;
        var avaluo = new AvaluoEspia(new FuenteAvaluoSimulada());
        await using var api = await MotorRunt.ArrancarAsync(b => avaluo.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST204")], ct, Fuente.Avaluo);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var antes = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST204", Fuente.Avaluo, ct);
        antes.Estado.Should().Be(EstadoCelda.Completado);

        var respuesta = await api.Cliente(MotorPersonas.Admin(api, empresa)).PostAsync($"{Program.Prefijo}/consultas/{antes.Id}/reprocesar", null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be("CELDA_NO_EN_ERROR");
        var despues = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST204", Fuente.Avaluo, ct);
        (despues.Estado, despues.Payload, despues.VersionTablero, despues.ActualizadoEn).Should().Be((antes.Estado, antes.Payload, antes.VersionTablero, antes.ActualizadoEn));
        avaluo.Llamadas.Should().ContainSingle("el reproceso rechazado no llama a la fuente");
        (await MotorPersonas.IntentosAsync(empresa, antes.Id, ct)).Should().ContainSingle();
    }

    // AC1 (F05-C7) — vehículo consultado por VIN sin placa asignada: el avalúo se pide por el VIN, sin placa, y completa; el
    // intento registra el VIN.
    [Fact]
    public async Task Por_VIN_sin_placa_consulta_por_VIN()
    {
        var ct = TestContext.Current.CancellationToken;
        var avaluo = new AvaluoEspia(new FuenteAvaluoSimulada());
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit());
            avaluo.Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(RuntParaSimit.VinSinPlaca)], ct, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var llamada = avaluo.Llamadas.Should().ContainSingle().Subject;
        (llamada.Empresa, llamada.Solicitud.Placa, llamada.Solicitud.Vin).Should().Be((empresa.Id, (string?)null, RuntParaSimit.VinSinPlaca));
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, RuntParaSimit.VinSinPlaca, Fuente.Avaluo, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Which.IdentificadorConsultado.Should().Be(RuntParaSimit.VinSinPlaca);
    }

    // AC4 (negativo, F05-C8) — AVALUO en ERROR por Timeout agotado (sin reintentos): el vehículo no queda bloqueado, RUNT y
    // SIMIT_VEHICULO completan y la celda queda en ERROR TIMEOUT reprocesable. La simulación responde Timeout con la placa ##7.
    [Fact]
    public async Task Timeout_agotado_no_bloquea_el_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var avaluo = new AvaluoEspia(new AvaluoConTimeout());
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit());
            b.Services.AddSingleton<IFuenteSimit>(new FuenteSimitSimulada());
            avaluo.Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST201")], ct, Fuente.SimitVehiculo, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST201", ct);
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct))
            .Should().Equal("AVALUO ERROR TIMEOUT", "RUNT COMPLETADO -", "SIMIT_VEHICULO COMPLETADO -");
        ClasificadorErrores.Clasificar("TIMEOUT").Should().Be(ClaseError.Transitorio, "el ERROR por Timeout se puede reprocesar");
        avaluo.Llamadas.Should().ContainSingle();
        var simulado = await new FuenteAvaluoSimulada().ConsultarAsync(empresa.Id, new SolicitudAvaluo("TST207", null, null, null, null, null), ct);
        simulado.Error!.TipoError.Should().Be("TIMEOUT", "la simulación responde Timeout con la placa ##7");
    }

    // Decisión de diseño: si core-consultas responde sin valor y TODAS las fuentes en ERROR, los proveedores no respondieron:
    // es HTTP_5XX transitorio (se reintenta), no «sin valor comercial».
    [Fact]
    public async Task Todas_las_fuentes_en_error_es_transitorio_y_no_sin_valor()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new AvaluosGrpcFalso(todasEnError: true);
        var fuente = new FuenteAvaluoReal(new ClienteConsultas(grpc, NullLogger<ClienteConsultas>.Instance));

        var r = await fuente.ConsultarAsync(Guid.NewGuid(), new SolicitudAvaluo("TST201", null, 2020, null, null, null), ct);

        (r.Resultado, r.Error?.TipoError).Should().Be(((ResultadoAvaluo?)null, "HTTP_5XX"));
        grpc.Llamadas.Should().ContainSingle().Which.Request.Cilindraje.Should().Be(0, "un dato técnico que el RUNT no trae va en 0");
    }
}

/// <summary>Espía de <see cref="IFuenteAvaluo"/> (HU #13795): anota empresa y solicitud y delega en la fuente interna.</summary>
public sealed class AvaluoEspia(IFuenteAvaluo interna) : IFuenteAvaluo
{
    private readonly ConcurrentQueue<(Guid Empresa, SolicitudAvaluo Solicitud)> _llamadas = new();

    public IReadOnlyCollection<(Guid Empresa, SolicitudAvaluo Solicitud)> Llamadas => _llamadas;

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IFuenteAvaluo>(this);

    public Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct)
    {
        _llamadas.Enqueue((empresa, solicitud));
        return interna.ConsultarAsync(empresa, solicitud, ct);
    }
}

/// <summary>Avalúo que siempre responde Timeout (HU #13795, AC4): no depende del último carácter de la placa.</summary>
public sealed class AvaluoConTimeout : IFuenteAvaluo
{
    public Task<RespuestaFuente<ResultadoAvaluo>> ConsultarAsync(Guid empresa, SolicitudAvaluo solicitud, CancellationToken ct) =>
        Task.FromResult(RespuestaFuente<ResultadoAvaluo>.Fallo(new ErrorConsulta(ErroresFuentePersona.Timeout, "Avalúo de prueba: tiempo agotado.")));
}

/// <summary>
/// Cliente gRPC falso de core-consultas para el avalúo (HU #13795): responde <c>ConsultarAvaluos</c> con Fasecolda (principal,
/// 48.500.000), MercadoLibre (47.200.000, 12 muestras) y base gravable sin datos (con un mensaje que no debe guardarse), o con
/// todas las fuentes en ERROR; anota la empresa de la metadata y la solicitud.
/// </summary>
public sealed class AvaluosGrpcFalso(bool todasEnError = false) : ConsultasService.ConsultasServiceClient
{
    private readonly ConcurrentQueue<(string? Tenant, ConsultarAvaluosRequest Request)> _llamadas = new();

    public IReadOnlyCollection<(string? Tenant, ConsultarAvaluosRequest Request)> Llamadas => _llamadas;

    /// <summary>Registra el adaptador real del avalúo sobre este cliente (antes que Program: gana al TryAdd).</summary>
    public void RegistrarAdaptadorReal(WebApplicationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Services.AddSingleton(new ClienteConsultas(this, NullLogger<ClienteConsultas>.Instance));
        b.Services.AddScoped<IFuenteAvaluo, FuenteAvaluoReal>();
    }

    public override AsyncUnaryCall<ConsultarAvaluosResponse> ConsultarAvaluosAsync(ConsultarAvaluosRequest request, CallOptions options)
    {
        _llamadas.Enqueue((options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata), request));
        var respuesta = new ConsultarAvaluosResponse();
        if (todasEnError)
        {
            respuesta.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Error });
            respuesta.Avaluos.Add(new Avaluo { Fuente = "mercado_libre", Estado = EstadoAvaluo.Error });
        }
        else
        {
            respuesta.ValorSugerido = 48_500_000;
            respuesta.FuentePrincipal = "fasecolda";
            respuesta.Avaluos.Add(new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 48_500_000 });
            respuesta.Avaluos.Add(new Avaluo { Fuente = "mercado_libre", Estado = EstadoAvaluo.Ok, Valor = 47_200_000, Muestras = 12 });
            respuesta.Avaluos.Add(new Avaluo { Fuente = "base_gravable", Estado = EstadoAvaluo.SinDatos, Mensaje = "MENSAJE DEL PROVEEDOR" });
        }

        return new AsyncUnaryCall<ConsultarAvaluosResponse>(
            Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}
