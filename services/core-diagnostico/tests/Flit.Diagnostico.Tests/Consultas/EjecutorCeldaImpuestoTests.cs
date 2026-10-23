using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Catalogos;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13862 (F09-H3), AC1-AC5 y AC7 (F09-C3, F09-C5, F09-C6, F09-C9) — celda IMPUESTO_DEPARTAMENTAL
/// (<see cref="EjecutorCeldaImpuesto"/>): consulta <see cref="IFuenteImpuesto"/> con la placa definitiva del RUNT y el departamento
/// derivado de su organismo, a nombre de la empresa de la celda; completa con <c>monto_total</c> y las vigencias sin el proveedor
/// (R-20); UNIMPLEMENTED (core-consultas sin C3) es <c>NO_CONECTADO</c> sin reintentos; Unavailable y DeadlineExceeded se
/// reintentan hasta <c>MaxReintentos</c>; sin placa o con el departamento ya no soportado no llama. Nunca contra core-consultas:
/// cliente gRPC falso (<see cref="ImpuestoGrpcFalso"/>, patrón <c>AvaluosGrpcFalso</c>). PostgreSQL real; placas TST### y el
/// organismo ficticio de <see cref="ImpuestoNoAplicaTests"/> en el departamento 05 (código DIVIPOLA público).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorCeldaImpuestoTests
/// </code>
/// </example>
public sealed class EjecutorCeldaImpuestoTests
{
    private const string Departamento = "05";
    private static readonly Fuente[] Alcance = [Fuente.Runt, Fuente.ImpuestoDepartamental];

    // AC1 (F09-C5) — TST001 particular con organismo del 05 soportado y el servidor falso con dos vigencias y 350000 pendientes:
    // la consulta sale con la placa, el departamento y la empresa; la celda completa con el monto y las vigencias y el intento
    // queda Success con el servicio del proveedor.
    [Fact]
    public async Task Consulta_con_placa_definitiva_y_departamento_derivado_y_completa_con_monto_y_vigencias()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ImpuestoGrpcFalso();
        await using var api = await ArrancarAsync(grpc.RegistrarAdaptadorReal, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var llamada = grpc.Llamadas.Should().ContainSingle().Subject;
        (llamada.Tenant, llamada.Request.Placa?.Valor, llamada.Request.DepartamentoCodigo).Should().Be((empresa.Id.ToString(), "TST001", Departamento));
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        (celda.Estado, celda.Origen).Should().Be((EstadoCelda.Completado, (OrigenCelda?)OrigenCelda.Api));
        using (var payload = JsonDocument.Parse(celda.Payload!))
        {
            var raiz = payload.RootElement;
            (raiz.GetProperty("monto_total").GetInt64(), raiz.GetProperty("departamento_codigo").GetString()).Should().Be((350_000L, Departamento));
            raiz.GetProperty("vigencias").EnumerateArray()
                .Select(v => (v.GetProperty("anio").GetInt32(), v.GetProperty("valor").GetInt64(), v.GetProperty("pagada").GetBoolean()))
                .Should().Equal((2025, 200_000L, false), (2026, 150_000L, false));
        }

        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.IdentificadorConsultado, intento.Servicio).Should().Be((ResultadoIntento.Success, "TST001", (ServicioConsulta?)ServicioConsulta.Verifik));
    }

    // AC2 (F09-C5, F09-C9) — el payload de la celda completada solo trae monto_total, vigencias y departamento_codigo: ni el
    // proveedor, ni la transacción, ni el origen. El intento sí lleva servicio y transacción (simulación: SIM-IMPUESTO).
    [Fact]
    public async Task El_payload_no_trae_proveedor_ni_transaccion_y_el_intento_si_trae_servicio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ArrancarAsync(b => b.Services.AddScoped<IFuenteImpuesto, FuenteImpuestoSimulada>(), ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        using (var payload = JsonDocument.Parse(celda.Payload!))
            payload.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["monto_total", "vigencias", "departamento_codigo"]);
        celda.Payload.Should().NotContainAny(["proveedor", "transaccion", "origen", FuenteImpuestoSimulada.Transaccion, "servicio"]);
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Servicio, intento.TransaccionProveedorId).Should().Be(((ServicioConsulta?)ServicioConsulta.Otro, FuenteImpuestoSimulada.Transaccion));

        // Adaptador real: el proveedor (verifik) va al intento y nunca al payload.
        var grpc = new ImpuestoGrpcFalso();
        var real = await new FuenteImpuestoReal(new ClienteConsultas(grpc, NullLogger<ClienteConsultas>.Instance))
            .ConsultarAsync(empresa.Id, new SolicitudImpuesto("TST001", Departamento), ct);
        real.Servicio.Should().Be(ServicioConsulta.Verifik);
        JsonSerializer.Serialize(real.Resultado, EjecutorPorPersonas.Json).Should().NotContainAny(["verifik", "proveedor", "chequeo"]);
    }

    // AC1 (F09-C5) — dos compañías con la misma placa: cada consulta lleva en x-flit-tenant-id la empresa de su celda.
    [Fact]
    public async Task Envia_x_flit_tenant_id_de_la_empresa_de_la_celda()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ImpuestoGrpcFalso();
        await using var api = await ArrancarAsync(grpc.RegistrarAdaptadorReal, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var dos = DosEmpresas.En(api);
        await MotorNa.LoteAsync(api, dos.A, [Lotes.Fila("TST001")], Alcance, null, null, ct);
        await MotorNa.LoteAsync(api, dos.B, [Lotes.Fila("TST001")], Alcance, null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Select(l => l.Tenant).Should().BeEquivalentTo([dos.A.Id.ToString(), dos.B.Id.ToString()],
            "cada compañía consulta a su nombre; nunca se reutiliza el dato de otra");
    }

    // AC4 (negativo, F09-C6) — core-consultas sin C3 responde UNIMPLEMENTED: ERROR NO_CONECTADO tras un único intento, sin
    // reintentos aunque MaxReintentos=3, y las demás celdas del vehículo siguen su curso.
    [Fact]
    public async Task UNIMPLEMENTED_queda_en_ERROR_NO_CONECTADO_sin_reintentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ImpuestoGrpcFalso { Falla = StatusCode.Unimplemented };
        var orden = new ConcurrentQueue<string>();
        await using var api = await ArrancarAsync(b =>
        {
            grpc.RegistrarAdaptadorReal(b);
            MotorNa.Espias(b, orden, Fuente.SimitVehiculo);
        }, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], [.. Alcance, Fuente.SimitVehiculo], null, null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 3), ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST001", ct);
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct))
            .Should().Equal("IMPUESTO_DEPARTAMENTAL ERROR NO_CONECTADO", "RUNT COMPLETADO -", "SIMIT_VEHICULO COMPLETADO -");
        grpc.Llamadas.Should().ContainSingle("NO_CONECTADO es definitivo: no se reintenta");
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        celda.IntentosAuto.Should().Be(0);
        var intento = (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.TipoError).Should().Be((ResultadoIntento.Error, "NO_CONECTADO"));
        TraductorErroresConsultas.Traducir(new RpcException(new Status(StatusCode.Unimplemented, string.Empty))).TipoError
            .Should().Be("ESTADO_NO_OK", "las demás fuentes conservan su traducción");
    }

    // AC5 (negativo, F09-C6) — Unavailable (RED) o DeadlineExceeded (TIMEOUT) siempre y MaxReintentos=1: dos intentos con ese
    // tipo, el segundo después del backoff, y la celda queda en ERROR reprocesable (el reproceso la acepta).
    [Theory]
    [InlineData(StatusCode.Unavailable, "RED")]
    [InlineData(StatusCode.DeadlineExceeded, "TIMEOUT")]
    public async Task Unavailable_y_DeadlineExceeded_se_reintentan_hasta_MaxReintentos_y_quedan_en_ERROR_reprocesable(StatusCode codigo, string tipo)
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var grpc = new ImpuestoGrpcFalso { Falla = codigo };
        await using var api = await ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            grpc.RegistrarAdaptadorReal(b);
        }, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);
        var procesador = Motor.Procesador(api, maxReintentos: 1);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);
        var enEspera = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        (enEspera.Estado, enEspera.IntentosAuto).Should().Be((EstadoCelda.Pendiente, 1), "queda el reintento");
        reloj.Avanzar(PoliticaReintentos.Espera(1));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        grpc.Llamadas.Should().HaveCount(2);
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Error, tipo));
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Select(i => (i.Estado, i.TipoError))
            .Should().Equal((ResultadoIntento.Error, tipo), (ResultadoIntento.Error, tipo));
        var reproceso = await api.Cliente(MotorPersonas.Admin(api, empresa)).PostAsync($"{Program.Prefijo}/consultas/{celda.Id}/reprocesar", null, ct);
        reproceso.StatusCode.Should().Be(HttpStatusCode.Accepted, await reproceso.Content.ReadAsStringAsync(ct));
    }

    // AC4/AC5 (F09-C6) — un chequeo en ERROR con OK (proveedor caído) es HTTP_5XX transitorio, con el servicio del proveedor;
    // FAILED_PRECONDITION (departamento no soportado en core-consultas) e InvalidArgument son ESTADO_NO_OK definitivos.
    [Fact]
    public async Task Chequeo_en_ERROR_del_proveedor_es_HTTP_5XX_transitorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var solicitud = new SolicitudImpuesto("TST001", Departamento);

        var caido = await Real(new ImpuestoGrpcFalso { ChequeoEnError = true }).ConsultarAsync(Guid.NewGuid(), solicitud, ct);
        (caido.Resultado, caido.Error?.TipoError, caido.Error?.EsTransitorio, caido.Servicio)
            .Should().Be(((ResultadoImpuesto?)null, "HTTP_5XX", (bool?)true, (ServicioConsulta?)ServicioConsulta.Verifik));

        foreach (var codigo in new[] { StatusCode.FailedPrecondition, StatusCode.InvalidArgument })
        {
            var r = await Real(new ImpuestoGrpcFalso { Falla = codigo }).ConsultarAsync(Guid.NewGuid(), solicitud, ct);
            (r.Error?.TipoError, r.Error?.EsTransitorio).Should().Be(("ESTADO_NO_OK", (bool?)false), codigo.ToString());
        }

        var sinC3 = await Real(new ImpuestoGrpcFalso { Falla = StatusCode.Unimplemented }).ConsultarAsync(Guid.NewGuid(), solicitud, ct);
        (sinC3.Error?.TipoError, sinC3.Error?.EsTransitorio).Should().Be(("NO_CONECTADO", (bool?)false));
        var noConectada = await new FuenteImpuestoNoConectada().ConsultarAsync(Guid.NewGuid(), solicitud, ct);
        (noConectada.Resultado, noConectada.Error?.TipoError).Should().Be(((ResultadoImpuesto?)null, "NO_CONECTADO"));
    }

    // Contexto técnico — sin placa definitiva tras el RUNT: ERROR SIN_PLACA, definitivo y sin llamar.
    [Fact]
    public async Task Sin_placa_queda_en_ERROR_SIN_PLACA_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ImpuestoGrpcFalso();
        await using var api = await ArrancarAsync(grpc.RegistrarAdaptadorReal, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);
        var procesador = Motor.Procesador(api, maxReintentos: 3);
        (await procesador.ProcesarLoteAsync(ct)).Should().Be(1, "primero solo la RUNT, que libera la del impuesto en PENDIENTE");
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, lote, "TST001", ct);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.vehiculos SET placa = NULL WHERE id = '{vehiculo.Id}'", ct);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        grpc.Llamadas.Should().BeEmpty();
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        (celda.Estado, celda.TipoError, celda.IntentosAuto).Should().Be((EstadoCelda.Error, EjecutorCeldaImpuesto.SinPlaca, 0));
        ClasificadorErrores.Clasificar(EjecutorCeldaImpuesto.SinPlaca).Should().Be(ClaseError.Definitivo);
    }

    // AC7 (negativo, F09-C3) — celda en PENDIENTE cuyo departamento pasó a no soportado antes de ejecutarse: N/A
    // DEPARTAMENTO_NO_SOPORTADO sin llamada ni consumo (ningún intento).
    [Fact]
    public async Task Departamento_que_dejo_de_estar_soportado_al_ejecutar_queda_NA_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new ImpuestoGrpcFalso();
        await using var api = await ArrancarAsync(grpc.RegistrarAdaptadorReal, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);
        var procesador = Motor.Procesador(api);
        (await procesador.ProcesarLoteAsync(ct)).Should().Be(1);
        (await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct)).Estado.Should().Be(EstadoCelda.Pendiente);
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(), new RelojManual(), ct))!.Soportados.Should().Be(0, "la capacidad ya no trae el 05");

        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        grpc.Llamadas.Should().BeEmpty();
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST001", Fuente.ImpuestoDepartamental, ct);
        (celda.Estado, celda.MotivoNa, celda.Payload).Should().Be((EstadoCelda.Na, (MotivoNa?)MotivoNa.DepartamentoNoSoportado, (string?)null));
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().BeEmpty("una N/A no deja intento ni consumo");
    }

    // AC3 (F09-C5) — celda COMPLETADO: un segundo disparo del mismo lote no la reabre ni llama; otro lote de la misma compañía
    // con la misma placa el mismo día reutiliza el dato con su fecha y hora, sin llamar.
    [Fact]
    public async Task Segundo_disparo_no_repite_una_celda_COMPLETADO_y_el_mismo_dia_se_reutiliza_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var grpc = new ImpuestoGrpcFalso();
        await using var api = await ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            grpc.RegistrarAdaptadorReal(b);
        }, ct);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        IrA(reloj, 8, 0);
        var primero = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")], Alcance, null, null, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var original = await MotorPersonas.CeldaAsync(api, empresa, primero, "TST001", Fuente.ImpuestoDepartamental, ct);
        original.Estado.Should().Be(EstadoCelda.Completado);

        await ReencolarAsync(api, empresa, primero, ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        var trasDisparo = await MotorPersonas.CeldaAsync(api, empresa, primero, "TST001", Fuente.ImpuestoDepartamental, ct);
        (trasDisparo.Id, trasDisparo.Estado, trasDisparo.Payload, trasDisparo.VersionTablero)
            .Should().Be((original.Id, EstadoCelda.Completado, original.Payload, original.VersionTablero));

        IrA(reloj, 15, 0);
        var segundo = await SegundoLoteAsync(api, empresa, "TST001", ct);
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Should().ContainSingle("ni el segundo disparo ni el otro lote del mismo día llaman al servidor");
        var copia = await MotorPersonas.CeldaAsync(api, empresa, segundo, "TST001", Fuente.ImpuestoDepartamental, ct);
        (copia.Estado, copia.ReutilizadaDeId, copia.Payload, copia.DatoObtenidoEn)
            .Should().Be((EstadoCelda.Completado, (Guid?)original.Id, original.Payload, original.DatoObtenidoEn));
        (await MotorPersonas.IntentosAsync(empresa, copia.Id, ct)).Should().ContainSingle().Which.DesdeCache.Should().BeTrue();
    }

    // Contexto técnico (FuenteImpuestoSimulada, solo LOCAL o DEV) — determinista por el último dígito: 0 sin deuda (vigencias
    // pagadas, monto 0), 7 TIMEOUT transitorio y el resto dos vigencias con deuda; siempre el mismo resultado.
    [Fact]
    public async Task Simulacion_determinista_por_ultimo_digito_de_la_placa()
    {
        var ct = TestContext.Current.CancellationToken;
        var simulada = new FuenteImpuestoSimulada();
        var empresa = Guid.NewGuid();

        var sinDeuda = await simulada.ConsultarAsync(empresa, new SolicitudImpuesto("TST300", Departamento), ct);
        sinDeuda.Resultado!.MontoTotal.Should().Be(0);
        sinDeuda.Resultado.Vigencias.Should().NotBeEmpty().And.OnlyContain(v => v.Pagada);
        sinDeuda.Resultado.DepartamentoCodigo.Should().Be(Departamento);

        var timeout = await simulada.ConsultarAsync(empresa, new SolicitudImpuesto("TST307", Departamento), ct);
        (timeout.Resultado, timeout.Error?.TipoError, timeout.Error?.EsTransitorio).Should().Be(((ResultadoImpuesto?)null, "TIMEOUT", (bool?)true));

        var conDeuda = await simulada.ConsultarAsync(empresa, new SolicitudImpuesto("TST301", "25"), ct);
        conDeuda.Resultado!.Vigencias.Should().HaveCount(2);
        conDeuda.Resultado.MontoTotal.Should().BePositive().And.Be(conDeuda.Resultado.Vigencias.Where(v => !v.Pagada).Sum(v => v.Valor));
        (conDeuda.Resultado.DepartamentoCodigo, conDeuda.TransaccionProveedorId).Should().Be(("25", FuenteImpuestoSimulada.Transaccion));
        var otraVez = await simulada.ConsultarAsync(empresa, new SolicitudImpuesto("TST301", "25"), ct);
        JsonSerializer.Serialize(otraVez.Resultado).Should().Be(JsonSerializer.Serialize(conDeuda.Resultado));
    }

    private static FuenteImpuestoReal Real(ImpuestoGrpcFalso grpc) => new(new ClienteConsultas(grpc, NullLogger<ClienteConsultas>.Instance));

    /// <summary>
    /// API con RUNT simulada de Intempo, el impuesto con la bandera encendida (<see cref="MotorNa"/>), el organismo ficticio en el
    /// departamento 05 y la réplica de departamentos con impuesto sincronizada con 05 soportado.
    /// </summary>
    internal static async Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder> configurar, CancellationToken ct)
    {
        var api = await MotorNa.ArrancarAsync(b =>
        {
            b.Services.AddScoped<ICatalogoOrganismosPlataforma, CatalogoOrganismosGrpc>();
            configurar(b);
        });
        if (api.Omitir is not null)
            return api;

        api.Catalogos.Organismos.Add(new OrganismoTransito
        {
            Id = Guid.NewGuid().ToString(),
            Codigo = "98001000",
            Nombre = "Secretaría de Tránsito de Prueba",
            DepartamentoCodigo = Departamento,
            Departamento = "DEPARTAMENTO DE PRUEBA",
            Activo = true,
        });
        await using (var scope = api.App.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct))!.Nuevos.Should().Be(1);
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(Departamento), new RelojManual(), ct))!.Soportados.Should().Be(1);
        return api;
    }

    /// <summary>Segundo disparo del lote: vuelve a encolar sus vehículos, como la confirmación (idempotente por vehículo y fuente).</summary>
    private static async Task ReencolarAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid lote, CancellationToken ct)
    {
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, lote, ct);
        await using var db = empresa.Db();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            await new EncoladorConsultas(db, TimeProvider.System).EncolarAsync(
                lote, [.. vehiculos.Select(v => new VehiculoAEncolar(v.Id, v.IdentificadorNorm, v.TipoIdentificador))], ct);
            await transaccion.CommitAsync(ct);
        });
    }

    /// <summary>Otro lote de la empresa (su configuración ya existe) con RUNT e impuesto, confirmado.</summary>
    private static async Task<Guid> SegundoLoteAsync(ApiDePrueba api, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta,
            Configuracion.ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote repetido", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila(placa)], ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        return id;
    }

    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

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
/// Cliente gRPC falso de core-consultas para el impuesto (HU #13862): <c>ConsultarImpuestoVehicular</c> responde dos vigencias
/// no pagadas (2025: 200.000 y 2026: 150.000; monto 350.000) con el proveedor <c>verifik</c>, un chequeo en ERROR
/// (<see cref="ChequeoEnError"/>) o lanza <see cref="Falla"/> (UNIMPLEMENTED = core-consultas sin C3). Anota la empresa de la
/// metadata y la solicitud.
/// </summary>
public sealed class ImpuestoGrpcFalso : ConsultasService.ConsultasServiceClient
{
    private readonly ConcurrentQueue<(string? Tenant, ConsultarImpuestoVehicularRequest Request)> _llamadas = new();

    public StatusCode? Falla { get; init; }

    public bool ChequeoEnError { get; init; }

    public IReadOnlyCollection<(string? Tenant, ConsultarImpuestoVehicularRequest Request)> Llamadas => _llamadas;

    /// <summary>Registra el adaptador real del impuesto sobre este cliente (antes que Program: gana al TryAdd).</summary>
    public void RegistrarAdaptadorReal(WebApplicationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Services.AddSingleton(new ClienteConsultas(this, NullLogger<ClienteConsultas>.Instance));
        b.Services.AddScoped<IFuenteImpuesto, FuenteImpuestoReal>();
    }

    public override AsyncUnaryCall<ConsultarImpuestoVehicularResponse> ConsultarImpuestoVehicularAsync(ConsultarImpuestoVehicularRequest request, CallOptions options)
    {
        _llamadas.Enqueue((options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata), request));
        if (Falla is { } codigo)
            throw new RpcException(new Status(codigo, "detalle del proveedor que no debe guardarse"));

        var resultado = new ResultadoConsulta { Proveedor = "verifik" };
        if (ChequeoEnError)
            resultado.Chequeos.Add(new Chequeo { Clave = "provider", Estado = EstadoChequeo.Error });
        var respuesta = new ConsultarImpuestoVehicularResponse { Resultado = resultado };
        if (!ChequeoEnError)
        {
            respuesta.MontoTotal = 350_000;
            respuesta.Vigencias.Add(new VigenciaImpuesto { Anio = 2026, Valor = 150_000, Pagada = false });
            respuesta.Vigencias.Add(new VigenciaImpuesto { Anio = 2025, Valor = 200_000, Pagada = false });
        }

        return new AsyncUnaryCall<ConsultarImpuestoVehicularResponse>(
            Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}
