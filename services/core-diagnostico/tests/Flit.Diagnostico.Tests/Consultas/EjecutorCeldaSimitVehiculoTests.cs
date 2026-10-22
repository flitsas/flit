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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13793 (F05-H4), AC1 y AC3-AC5 (F05-C1, F05-C6, F05-C10, F05-C11, F05-C14) — celda SIMIT_VEHICULO
/// (<see cref="EjecutorCeldaSimitVehiculo"/>): consulta <see cref="IFuenteSimit"/> por la placa definitiva que dejó el RUNT;
/// sin placa queda en ERROR <c>SIN_PLACA</c> sin llamar; el Timeout se reintenta con backoff y la respuesta inválida no; y la
/// reutilización del mismo día solo copia el dato de la misma compañía. El RUNT es un doble (<see cref="RuntParaSimit"/>) que
/// responde bien a toda placa TST1## para que el último carácter solo gobierne el SIMIT simulado. PostgreSQL real; placas
/// TST1## y documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorCeldaSimitVehiculoTests
/// </code>
/// </example>
public sealed class EjecutorCeldaSimitVehiculoTests
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    // AC1 (F05-C6, F05-C1) — RUNT COMPLETADO con placa TST102 en simulación: la celda queda COMPLETADO con dos comparendos
    // normalizados, la fuente se llamó con la placa y el intento la registra.
    [Fact]
    public async Task Consulta_por_la_placa_definitiva_del_RUNT()
    {
        var ct = TestContext.Current.CancellationToken;
        var simit = new SimitEspia(new FuenteSimitSimulada());
        await using var api = await ArrancarAsync(b => simit.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST102")], ct, Fuente.SimitVehiculo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        simit.Llamadas.Should().ContainSingle().Which.Should().Be((empresa.Id, "TST102", (string?)null));
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST102", Fuente.SimitVehiculo, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        celda.DatoObtenidoEn.Should().NotBeNull();
        using var payload = JsonDocument.Parse(celda.Payload!);
        payload.RootElement.GetProperty("cantidad_comparendos").GetInt32().Should().Be(2);
        var comparendos = payload.RootElement.GetProperty("comparendos").EnumerateArray().ToList();
        comparendos.Should().HaveCount(2);
        comparendos.Should().OnlyContain(c => c.EnumerateObject().Select(p => p.Name).All(n => n == "numero" || n == "fecha" || n == "valor" || n == "estado" || n == "organismo"));
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.IdentificadorConsultado, intento.PersonaId).Should().Be((ResultadoIntento.Success, "TST102", (Guid?)null));
        intento.Servicio.Should().NotBeNull("el intento registra quién respondió");
    }

    // AC4 (F05-C6) — un vehículo sin placa tras el RUNT (consultado por VIN, sin placa asignada): ERROR SIN_PLACA, definitivo,
    // sin llamar a la fuente ni consumir (intento sin servicio) y sin reintentos aunque queden.
    [Fact]
    public async Task Sin_placa_queda_error_definitivo_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var simit = new SimitEspia(new FuenteSimitSimulada());
        await using var api = await ArrancarAsync(b => simit.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(RuntParaSimit.VinSinPlaca)], ct, Fuente.SimitVehiculo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 3), ct);

        simit.Llamadas.Should().BeEmpty("sin placa no se llama a la fuente");
        (await MotorPersonas.CeldaAsync(api, empresa, lote, RuntParaSimit.VinSinPlaca, Fuente.Runt, ct)).Estado.Should().Be(EstadoCelda.Completado);
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, RuntParaSimit.VinSinPlaca, Fuente.SimitVehiculo, ct);
        (celda.Estado, celda.TipoError, celda.IntentosAuto).Should().Be((EstadoCelda.Error, EjecutorCeldaSimitVehiculo.SinPlaca, 0));
        ClasificadorErrores.Clasificar(EjecutorCeldaSimitVehiculo.SinPlaca).Should().Be(ClaseError.Definitivo);
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.TipoError, intento.Servicio, intento.IdentificadorConsultado)
            .Should().Be((ResultadoIntento.Error, EjecutorCeldaSimitVehiculo.SinPlaca, (ServicioConsulta?)null, (string?)null), "sin llamada no hay consumo");
    }

    // AC3 (F05-C10) — TST107 (Timeout) con MaxReintentos=1: se reintenta una vez después del backoff y luego queda en ERROR
    // reprocesable; TST108 (respuesta inválida) es definitivo: un solo intento y ERROR sin reintento.
    [Fact]
    public async Task Timeout_se_reintenta_y_respuesta_invalida_no()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var simit = new SimitEspia(new FuenteSimitSimulada());
        await using var api = await ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            simit.Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST107"), Lotes.Fila("TST108")], ct, Fuente.SimitVehiculo);
        var procesador = Motor.Procesador(api, maxReintentos: 1);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        var timeout = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST107", Fuente.SimitVehiculo, ct);
        var primero = (await MotorPersonas.IntentosAsync(empresa, timeout.Id, ct)).Should().ContainSingle().Subject;
        (timeout.Estado, timeout.IntentosAuto, timeout.TipoError).Should().Be((EstadoCelda.Pendiente, 1, (string?)null), "queda un reintento");
        timeout.DisponibleDesde.Should().Be(primero.Fin + PoliticaReintentos.Espera(1), "backoff desde el fin del intento");
        var invalida = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST108", Fuente.SimitVehiculo, ct);
        (invalida.Estado, invalida.TipoError, invalida.IntentosAuto).Should().Be((EstadoCelda.Error, "RESPUESTA_INVALIDA", 0), "un definitivo no se reintenta");
        (await procesador.ProcesarLoteAsync(ct)).Should().Be(0, "antes del backoff no se toma");

        reloj.Avanzar(PoliticaReintentos.Espera(1));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        var agotada = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST107", Fuente.SimitVehiculo, ct);
        (agotada.Estado, agotada.TipoError, agotada.IntentosAuto).Should().Be((EstadoCelda.Error, "TIMEOUT", 1), "agotado el reintento queda en ERROR reprocesable");
        var intentos = await MotorPersonas.IntentosAsync(empresa, agotada.Id, ct);
        intentos.Should().HaveCount(2).And.OnlyContain(i => i.Estado == ResultadoIntento.Error && i.TipoError == "TIMEOUT" && i.IdentificadorConsultado == "TST107");
        intentos[1].Inicio.Should().BeOnOrAfter(intentos[0].Fin + PoliticaReintentos.Espera(1));
        (await MotorPersonas.IntentosAsync(empresa, invalida.Id, ct)).Should().ContainSingle();
        simit.Llamadas.Select(l => l.Placa).Should().BeEquivalentTo(["TST107", "TST107", "TST108"]);
    }

    // AC5 (F05-C11, F05-C14, fuga) — la compañía B consultó hoy SIMIT_VEHICULO de TST102: A hace su propia consulta (con su
    // empresa en la metadata, por placa y sin documento) y no reutiliza el dato de B; un segundo lote de A el mismo día sí
    // reutiliza el dato de A sin llamar.
    [Fact]
    public async Task Reutiliza_el_dato_del_mismo_dia_de_la_misma_compania_y_no_de_otra()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var grpc = new MultasGrpcFalso();
        await using var api = await ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            grpc.RegistrarAdaptadorReal(b);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var dos = DosEmpresas.En(api);

        IrA(reloj, 8, 0);
        var deB = await MotorRunt.LoteAsync(api, dos.B, [Lotes.Fila("TST102")], ct, Fuente.SimitVehiculo);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        IrA(reloj, 9, 0);
        var deA = await MotorRunt.LoteAsync(api, dos.A, [Lotes.Fila("TST102")], ct, Fuente.SimitVehiculo);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Should().BeEquivalentTo(
            [(dos.B.Id.ToString(), "TST102", false, TipoPersona.Unspecified), (dos.A.Id.ToString(), "TST102", false, TipoPersona.Unspecified)],
            "cada compañía consulta por su cuenta, por placa y sin documento");
        var celdaB = await MotorPersonas.CeldaAsync(api, dos.B, deB, "TST102", Fuente.SimitVehiculo, ct);
        var celdaA = await MotorPersonas.CeldaAsync(api, dos.A, deA, "TST102", Fuente.SimitVehiculo, ct);
        (celdaA.Estado, celdaA.ReutilizadaDeId).Should().Be((EstadoCelda.Completado, (Guid?)null), "nunca se reutiliza el dato de otra compañía");
        celdaB.Estado.Should().Be(EstadoCelda.Completado);
        (await MotorPersonas.IntentosAsync(dos.A, celdaA.Id, ct)).Should().ContainSingle().Which.DesdeCache.Should().BeFalse();
        (await MotorPersonas.IntentosAsync(dos.A, celdaB.Id, ct)).Should().BeEmpty("A no ve los intentos de B");

        IrA(reloj, 10, 0);
        var otroDeA = await SegundoLoteAsync(api, dos.A, "TST102", ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Should().HaveCount(2, "el segundo lote de A reutiliza el dato de hoy de A");
        var copia = await MotorPersonas.CeldaAsync(api, dos.A, otroDeA, "TST102", Fuente.SimitVehiculo, ct);
        (copia.Estado, copia.ReutilizadaDeId, copia.Payload).Should().Be((EstadoCelda.Completado, (Guid?)celdaA.Id, celdaA.Payload));
        var intento = (await MotorPersonas.IntentosAsync(dos.A, copia.Id, ct)).Should().ContainSingle().Subject;
        (intento.DesdeCache, intento.Servicio).Should().Be((true, (ServicioConsulta?)null));
    }

    /// <summary>API con el RUNT de <see cref="RuntParaSimit"/>; el ejecutor SIMIT es el real del servicio.</summary>
    private static Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder> configurar) =>
        MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IFuenteRunt>(new RuntParaSimit());
            configurar(b);
        });

    /// <summary>Otro lote de la compañía (configuración ya sembrada) con RUNT y SIMIT_VEHICULO, confirmado.</summary>
    private static async Task<Guid> SegundoLoteAsync(ApiDePrueba api, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta,
            ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote repetido", "SIMPLE", null, ["RUNT", "SIMIT_VEHICULO"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila(placa)], ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
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
/// Espía de <see cref="IFuenteSimit"/> (HU #13793): anota empresa, placa y tipo de documento de cada llamada y delega en la
/// fuente interna (la simulada, por defecto).
/// </summary>
public sealed class SimitEspia(IFuenteSimit interna) : IFuenteSimit
{
    private readonly ConcurrentQueue<(Guid Empresa, string? Placa, string? TipoDocumento)> _llamadas = new();

    public IReadOnlyCollection<(Guid Empresa, string? Placa, string? TipoDocumento)> Llamadas => _llamadas;

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IFuenteSimit>(this);

    public Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct)
    {
        _llamadas.Enqueue((empresa, solicitud.Placa, solicitud.Documento?.Tipo));
        return interna.ConsultarAsync(empresa, solicitud, ct);
    }
}

/// <summary>
/// RUNT de las pruebas SIMIT (HU #13793): toda placa responde con un propietario natural ficticio y esa placa; el VIN
/// <see cref="VinSinPlaca"/> responde sin placa asignada.
/// </summary>
public sealed class RuntParaSimit : IFuenteRunt
{
    public const string VinSinPlaca = "9ZZPRUEBA00000109";

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identificador);
        var placa = identificador.EsVin ? null : identificador.Valor;
        var vin = identificador.EsVin ? identificador.Valor : "9ZZPRUEBA00000100";
        return Task.FromResult(ResultadoRunt.Exito(new VehiculoRunt(
            placa, vin, "PARTICULAR", "AUTOMOVIL", "SECRETARIA DE TRANSITO DE PRUEBA",
            [new PropietarioRunt("CC", "9999900101", TipoPropiedad.Propio, [])], [], "INTEMPO_SIMULADO", "TX-PRUEBA-SIMIT")));
    }
}

/// <summary>
/// Cliente gRPC falso de core-consultas para SIMIT (HU #13793): responde <c>ConsultarMultas</c> con dos comparendos y datos del
/// infractor inventados (que el adaptador debe descartar) y anota la empresa de la metadata, la placa, si vino documento y el
/// tipo de persona.
/// </summary>
public sealed class MultasGrpcFalso : ConsultasService.ConsultasServiceClient
{
    private readonly ConcurrentQueue<(string? Tenant, string Placa, bool ConDocumento, TipoPersona TipoPersona)> _llamadas = new();

    public IReadOnlyCollection<(string? Tenant, string Placa, bool ConDocumento, TipoPersona TipoPersona)> Llamadas => _llamadas;

    /// <summary>Registra el adaptador real de SIMIT sobre este cliente (antes que Program: gana al TryAdd).</summary>
    public void RegistrarAdaptadorReal(WebApplicationBuilder b)
    {
        b.Services.AddSingleton(new ClienteConsultas(this, NullLogger<ClienteConsultas>.Instance));
        b.Services.AddScoped<IFuenteSimit, FuenteSimitReal>();
    }

    public override AsyncUnaryCall<ConsultarMultasResponse> ConsultarMultasAsync(ConsultarMultasRequest request, CallOptions options)
    {
        _llamadas.Enqueue((options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata), request.Placa?.Valor ?? string.Empty,
            request.Documento is not null, request.TipoPersona));
        var resultado = new ResultadoConsulta { Proveedor = "smartcloud_simit", Semaforo = Semaforo.Rojo };
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = "PERSONA PRUEBA INFRACTORA" });
        var multas = new Chequeo { Clave = "multas", Etiqueta = "Multas", Estado = EstadoChequeo.Fail, Fuente = "SIMIT" };
        multas.Comparendos.Add(new Comparendo { Numero = "C-1021", Fecha = "2026-01-10", Valor = "604100.00", Estado = "PENDIENTE", Organismo = "TRANSITO FICTICIO" });
        multas.Comparendos.Add(new Comparendo { Numero = "C-1022", Fecha = "2026-02-11", Valor = "302050.00", Estado = "PENDIENTE", Organismo = "TRANSITO FICTICIO" });
        resultado.Chequeos.Add(multas);
        return new AsyncUnaryCall<ConsultarMultasResponse>(
            Task.FromResult(new ConsultarMultasResponse { Resultado = resultado }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}
