using System.Collections.Concurrent;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13737 (F03-H6), AC2-AC4 (F03-C12, D-12) — reintentos de errores transitorios con backoff sobre la cola durable:
/// <c>disponible_desde</c> y el reloj inyectable (<see cref="RelojManual"/>), sin esperas reales. Mientras quedan reintentos la
/// celda RUNT vuelve a PENDIENTE y las demás celdas del vehículo siguen esperando sin bloquearse; agotados, la RUNT queda en
/// ERROR reprocesable y se aplica el bloqueo de #13730. Un definitivo no se reintenta. <c>Diagnostico:Consultas:MaxReintentos</c>
/// fuera de [0, 3] impide arrancar. PostgreSQL real (<see cref="ApiDePrueba"/>), fuente RUNT simulada (TST007 = Timeout).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReintentosTests
/// </code>
/// </example>
public sealed class ReintentosTests
{
    // AC2 (F03-C12) — Timeout siempre: con MaxReintentos=n hay n+1 intentos ERROR, cada reintento después de su backoff, y la
    // celda queda en ERROR reprocesable; hasta agotarse, SIMIT sigue esperando sin bloquearse.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Transitorio_se_reintenta_hasta_MaxReintentos_y_queda_ERROR(int maxReintentos)
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            new FuenteRuntEspia(orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST007")], ct, Fuente.SimitVehiculo);
        var procesador = Motor.Procesador(api, maxReintentos: maxReintentos);

        for (var reintento = 1; reintento <= maxReintentos; reintento++)
        {
            await MotorRunt.ProcesarTodoAsync(procesador, ct);
            var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
            var runt = celdas.Single(c => c.Fuente == Fuente.Runt);
            var simit = celdas.Single(c => c.Fuente == Fuente.SimitVehiculo);
            var ultimo = (await IntentosAsync(api, empresa, id, ct))[^1];
            runt.Estado.Should().Be(EstadoCelda.Pendiente, $"queda el reintento {reintento} de {maxReintentos}");
            runt.TipoError.Should().BeNull();
            runt.LeaseHasta.Should().BeNull();
            runt.IntentosAuto.Should().Be(reintento);
            runt.DisponibleDesde.Should().Be(ultimo.Fin + PoliticaReintentosEspera(reintento), "backoff 5 s × 2^(n−1) desde el fin del intento");
            simit.Estado.Should().Be(EstadoCelda.Pendiente, "las demás celdas siguen esperando a RUNT, sin bloquearse");
            simit.TipoError.Should().BeNull();
            (await procesador.ProcesarLoteAsync(ct)).Should().Be(0, "antes del backoff no se toma");
            reloj.Avanzar(PoliticaReintentosEspera(reintento));
        }

        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        orden.Should().HaveCount(maxReintentos + 1).And.OnlyContain(o => o == "RUNT PLACA_CARRO TST007", "SIMIT nunca se llamó");
        var intentos = await IntentosAsync(api, empresa, id, ct);
        intentos.Should().HaveCount(maxReintentos + 1);
        intentos.Should().OnlyContain(i => i.Estado == ResultadoIntento.Error && i.TipoError == "TIMEOUT" && i.Disparador == DisparadorIntento.Auto);
        for (var k = 1; k < intentos.Count; k++)
            intentos[k].Inicio.Should().BeOnOrAfter(intentos[k - 1].Fin + PoliticaReintentosEspera(k), "cada reintento va después de su backoff");
        var finales = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        var runtFinal = finales.Single(c => c.Fuente == Fuente.Runt);
        runtFinal.Estado.Should().Be(EstadoCelda.Error, "agotados los reintentos queda en ERROR reprocesable");
        runtFinal.TipoError.Should().Be("TIMEOUT");
        runtFinal.IntentosAuto.Should().Be(maxReintentos);
        var simitFinal = finales.Single(c => c.Fuente == Fuente.SimitVehiculo);
        simitFinal.Estado.Should().Be(EstadoCelda.Error);
        simitFinal.TipoError.Should().Be(OrquestadorVehiculo.BloqueadaPorRunt, "agotada la RUNT se aplica el bloqueo de #13730");
    }

    // AC3 (F03-C12) — un definitivo deja un solo intento y la celda en ERROR, sin reintento, aunque queden reintentos.
    [Theory]
    [InlineData("TST008", "simulada", "RESPUESTA_INVALIDA")]
    [InlineData("TST001", "no-conectada", "NO_CONECTADO")]
    public async Task Definitivo_no_se_reintenta(string placa, string fuenteRunt, string tipoError)
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        IFuenteRunt interna = fuenteRunt == "no-conectada" ? new FuenteRuntNoConectada() : new FuenteRuntIntempoSimulada();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden, interna).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(placa)], ct, Fuente.SimitVehiculo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 3), ct);

        orden.Should().ContainSingle();
        var intento = (await IntentosAsync(api, empresa, id, ct)).Should().ContainSingle().Subject;
        intento.Estado.Should().Be(ResultadoIntento.Error);
        intento.TipoError.Should().Be(tipoError);
        intento.MensajeError.Should().NotBeNullOrWhiteSpace();
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        var runt = celdas.Single(c => c.Fuente == Fuente.Runt);
        runt.Estado.Should().Be(EstadoCelda.Error);
        runt.TipoError.Should().Be(tipoError);
        runt.IntentosAuto.Should().Be(0, "un definitivo no consume reintentos");
        celdas.Single(c => c.Fuente == Fuente.SimitVehiculo).TipoError.Should().Be(OrquestadorVehiculo.BloqueadaPorRunt);
    }

    // AC4 (F03-C12) — MaxReintentos fuera de [0, 3] impide arrancar e indica el rango permitido.
    [Theory]
    [InlineData("-1")]
    [InlineData("4")]
    public async Task MaxReintentos_fuera_de_rango_impide_arrancar(string valor)
    {
        var error = await FluentActions.Awaiting(() => ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration["Diagnostico:Consultas:MaxReintentos"] = valor))
            .Should().ThrowAsync<OptionsValidationException>();
        error.Which.Message.Should().Contain("Diagnostico:Consultas:MaxReintentos").And.Contain("[0, 3]");
    }

    // D-12 — sin configuración, MaxReintentos vale 1; 0 y 3 son válidos.
    [Theory]
    [InlineData(null, 1)]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    public async Task MaxReintentos_por_defecto_es_1_y_acepta_el_rango(string? valor, int esperado)
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            if (valor is not null)
                b.Configuration["Diagnostico:Consultas:MaxReintentos"] = valor;
        });
        api.App.Services.GetRequiredService<IOptions<OpcionesConsultas>>().Value.MaxReintentos.Should().Be(esperado);
    }

    private static TimeSpan PoliticaReintentosEspera(int reintento) => PoliticaReintentos.Espera(reintento);

    /// <summary>Intentos del lote en orden de inicio, leídos con el contexto de la empresa.</summary>
    internal static async Task<List<IntentoConsulta>> IntentosAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid diagnostico, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.DiagnosticoId == diagnostico).OrderBy(i => i.Inicio).ToListAsync(ct);
    }
}
