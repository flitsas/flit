using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Catalogos;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13733 (F03-H5), AC1, AC3 y AC4 — al completar la RUNT de un vehículo, las celdas que no aplican quedan en NA con su
/// motivo en la misma transacción que libera las demás, sin llamar a ninguna fuente ni consumir; las celdas de persona
/// (R-24) tienen su propio semáforo y su fallo no bloquea al vehículo (solo RUNT bloquea). Fuente RUNT simulada de Intempo
/// (escenario 5 = servicio público, 1 = persona natural sin locatario, 3 = leasing con locatario). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~NoAplicaTests
/// </code>
/// </example>
public sealed class NoAplicaTests
{
    // AC1 (F03-C9) y AC3 (F03-C6) — servicio público sin locatario: impuesto NA SERVICIO_PUBLICO y las del locatario NA
    // SIN_DOCUMENTO_LOCATARIO, sin ninguna llamada a sus adaptadores ni intento.
    [Fact]
    public async Task Celda_NA_no_genera_llamada_ni_consumo()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden).Registrar(b.Services);
            MotorNa.Espias(b, orden, Fuente.ImpuestoDepartamental, Fuente.RuntLocatario, Fuente.SimitLocatario);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST005")],
            [Fuente.Runt, Fuente.ImpuestoDepartamental], null, [Fuente.RuntLocatario, Fuente.SimitLocatario], ct);
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST005", ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        orden.Should().Equal(["RUNT PLACA_CARRO TST005"], "las celdas NA no llaman a su adaptador");
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Select(c => $"{CodigosDeEnum.Codigo(c.Fuente)} {CodigosDeEnum.Codigo(c.Estado)} {(c.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : "-")}")
            .Should().BeEquivalentTo(
            [
                "RUNT COMPLETADO -",
                "IMPUESTO_DEPARTAMENTAL NA SERVICIO_PUBLICO",
                "RUNT_LOCATARIO NA SIN_DOCUMENTO_LOCATARIO",
                "SIMIT_LOCATARIO NA SIN_DOCUMENTO_LOCATARIO",
            ]);
        celdas.Select(c => c.VersionTablero).Distinct().Should().ContainSingle("las NA se cierran en la transacción que cierra RUNT");
        celdas.Where(c => c.Estado == EstadoCelda.Na).Should().OnlyContain(c => c.Payload == null && c.Origen == null && c.IntentosAuto == 0 && c.LeaseHasta == null);
        (await Motor.EscalarAsync(api, $"SELECT count(*) FROM diagnostico.intentos_consulta WHERE vehiculo_id = '{vehiculo.Id}' AND fuente <> 'RUNT'", ct))
            .Should().Be(0, "una celda NA no deja intento");
        (await MotorRunt.VehiculoAsync(api, empresa, id, "TST005", ct)).Servicio.Should().Be("PUBLICO");
    }

    // AC3 (F03-C6) — con documento del locatario (declarado o del proveedor) sus celdas se consultan.
    [Fact]
    public async Task Con_documento_de_locatario_sus_celdas_se_consultan()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorNa.ArrancarAsync(b => MotorNa.Espias(b, orden, Fuente.RuntLocatario, Fuente.SimitLocatario));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001", "CC", "9999900077"), Lotes.Fila("TST003"), Lotes.Fila("TST011")],
            [Fuente.Runt], null, [Fuente.RuntLocatario, Fuente.SimitLocatario], ct);
        var declarado = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        var proveedor = await MotorRunt.VehiculoAsync(api, empresa, id, "TST003", ct);
        var sinDocumento = await MotorRunt.VehiculoAsync(api, empresa, id, "TST011", ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        foreach (var conDocumento in new[] { declarado, proveedor })
        {
            (await MotorRunt.CeldasDelVehiculoAsync(api, conDocumento.Id, ct)).Should().BeEquivalentTo(
                ["RUNT COMPLETADO -", "RUNT_LOCATARIO COMPLETADO -", "SIMIT_LOCATARIO COMPLETADO -"]);
            orden.Should().Contain($"RUNT_LOCATARIO {conDocumento.Id}").And.Contain($"SIMIT_LOCATARIO {conDocumento.Id}");
        }

        orden.Should().NotContain(o => o.EndsWith(sinDocumento.Id.ToString(), StringComparison.Ordinal));
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Where(c => c.VehiculoId == sinDocumento.Id && c.Fuente != Fuente.Runt)
            .Should().OnlyContain(c => c.Estado == EstadoCelda.Na && c.MotivoNa == MotivoNa.SinDocumentoLocatario);
    }

    // AC4 (F03-C8) — RUNT_PROPIETARIO falla: esa celda queda en ERROR con su propio intento y las demás del vehículo siguen.
    [Fact]
    public async Task Falla_de_RUNT_PROPIETARIO_no_bloquea_al_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        var propietario = new EjecutorQueFalla(Fuente.RuntPropietario, "RESPUESTA_INVALIDA");
        await using var api = await MotorNa.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IEjecutorCelda>(propietario);
            MotorNa.Espias(b, orden, Fuente.SimitVehiculo, Fuente.SimitPropietario);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001")],
            [Fuente.Runt, Fuente.SimitVehiculo], [Fuente.RuntPropietario, Fuente.SimitPropietario], null, ct);
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().BeEquivalentTo(
            ["RUNT COMPLETADO -", "RUNT_PROPIETARIO ERROR RESPUESTA_INVALIDA", "SIMIT_VEHICULO COMPLETADO -", "SIMIT_PROPIETARIO COMPLETADO -"]);
        propietario.Llamadas.Should().Be(1, "la celda del propietario tuvo su propio intento");
        orden.Should().Contain($"SIMIT_VEHICULO {vehiculo.Id}").And.Contain($"SIMIT_PROPIETARIO {vehiculo.Id}");
    }

    /// <summary>Ejecutor de prueba que deja su celda en ERROR con el tipo dado y cuenta sus llamadas.</summary>
    private sealed class EjecutorQueFalla(Fuente fuente, string tipoError) : IEjecutorCelda
    {
        private int _llamadas;

        public Fuente Fuente => fuente;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            return Task.FromResult(ResultadoCelda.Error(tipoError));
        }
    }
}

/// <summary>Ayudas de las pruebas de no aplica y de organismos (HU #13733).</summary>
internal static class MotorNa
{
    /// <summary>Departamento ficticio (96 no es código DIVIPOLA) que deja la réplica del impuesto con un soportado (Q5).</summary>
    public const string DepartamentoSoportadoFicticio = "96";

    /// <summary>
    /// API con la fuente RUNT simulada de Intempo y el impuesto disponible, salvo que se pida lo contrario. Desde HU #13863 (Q5)
    /// disponible exige bandera en true, adaptador conectado (el doble de <see cref="ApiDePrueba"/> lo da) y al menos un
    /// departamento soportado en la réplica: se siembra <see cref="DepartamentoSoportadoFicticio"/>, que ningún vehículo de
    /// prueba deriva, así que no cambia qué celdas quedan N/A.
    /// </summary>
    public static async Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder>? configurar = null, bool impuestoDisponible = true)
    {
        var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DisponibilidadFuentes.ClaveImpuestoDisponible] = impuestoDisponible ? "true" : "false",
            });
            configurar?.Invoke(b);
        });
        if (impuestoDisponible && api.Omitir is null)
            await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(DepartamentoSoportadoFicticio), new RelojManual(), TestContext.Current.CancellationToken);
        return api;
    }

    /// <summary>Registra un ejecutor de prueba (anota «FUENTE vehiculo» y completa) por cada fuente dada.</summary>
    public static void Espias(WebApplicationBuilder b, ConcurrentQueue<string> orden, params Fuente[] fuentes)
    {
        foreach (var fuente in fuentes)
            new EjecutorFuenteDePrueba(fuente, orden).Registrar(b.Services);
    }

    /// <summary>Siembra la configuración con las fuentes del alcance, crea la cabecera con sus tres grupos y confirma (202).</summary>
    public static async Task<Guid> LoteAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, Fuente[] porVehiculo,
        Fuente[]? porPropietario, Fuente[]? porLocatario, CancellationToken ct)
    {
        await SembrarAsync(empresa, new ConfiguracionEmpresa { FuentesHabilitadas = [.. porVehiculo, .. porPropietario ?? [], .. porLocatario ?? []] }, ct);
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        static string[] Codigos(Fuente[]? f) => [.. (f ?? []).Select(CodigosDeEnum.Codigo)];
        var creada = await cliente.PostAsync(Cabeceras.Ruta,
            Cuerpo(Cabeceras.Json("Lote no aplica", "SIMPLE", null, Codigos(porVehiculo), Codigos(porPropietario), Codigos(porLocatario))), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, filas, ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        return id;
    }
}
