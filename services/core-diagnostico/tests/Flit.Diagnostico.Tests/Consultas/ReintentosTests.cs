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
        var procesador = Motor.Bus(api, maxReintentos: maxReintentos);

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
            runt.DisponibleDesde.Should().Be(ultimo.Fin + PoliticaReintentosEspera(reintento), "backoff 1/2/4 min (factor 0 en las pruebas) desde el fin del intento");
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

        await MotorRunt.ProcesarTodoAsync(Motor.Bus(api, maxReintentos: 3), ct);

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

    // HU #14752 (D5 = a) — por defecto 3 reintentos, base de 1 min, ±20 %, 60 s de mínimo para el límite y 10 min para «pegada».
    [Fact]
    public void Por_defecto_3_reintentos_base_de_1_minuto_20_por_ciento_60_segundos_de_limite_y_10_de_umbral()
    {
        var porDefecto = new OpcionesConsultas();
        (porDefecto.MaxReintentos, porDefecto.EsperaReintento, porDefecto.UmbralPegada)
            .Should().Be((3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)));
        (porDefecto.FactorAleatorio, porDefecto.EsperaMinimaLimiteProveedor).Should().Be((0.2, TimeSpan.FromMinutes(1)));
        porDefecto.Errores().Should().BeEmpty();
    }

    // HU #14752 — el segundo y el tercer reintento esperan 2 y 4 min (factor 0 en la configuración de las pruebas).
    [Fact]
    public void Segundo_y_tercer_reintento_a_2_y_4_minutos()
    {
        PoliticaReintentosEspera(1).Should().Be(TimeSpan.FromMinutes(1));
        PoliticaReintentosEspera(2).Should().Be(TimeSpan.FromMinutes(2));
        PoliticaReintentosEspera(3).Should().Be(TimeSpan.FromMinutes(4));
        ApiDePrueba.Configuracion("sin-base")["Diagnostico:Consultas:FactorAleatorio"].Should().Be("0", "las pruebas del motor corren sin azar");
        var ahora = DateTimeOffset.UnixEpoch;
        var tres = new PoliticaReintentos(3, PoliticaReintentos.EsperaPorDefecto, 0, PoliticaReintentos.EsperaMinimaLimitePorDefecto);
        tres.Decidir("TIMEOUT", 1, ahora, 0.0).DisponibleDesde.Should().Be(ahora.AddMinutes(2));
        tres.Decidir("TIMEOUT", 2, ahora, 1.0).DisponibleDesde.Should().Be(ahora.AddMinutes(4));
    }

    // HU #14752 — reglas de las claves nuevas: el factor en [0, 0,5], el mínimo del límite en [1 s, 10 min] y el umbral de pegada
    // mayor que la espera MÁXIMA EFECTIVA (con la base de 2 min: 2 × 4 × 1,2 = 9,6 min > 9 min).
    [Fact]
    public void Validacion_del_factor_del_minimo_y_del_umbral_contra_la_espera_maxima_efectiva()
    {
        new OpcionesConsultas { FactorAleatorio = -0.1 }.Errores().Should().ContainSingle().Which.Should().Contain(nameof(OpcionesConsultas.FactorAleatorio));
        new OpcionesConsultas { FactorAleatorio = 0.51 }.Errores().Should().ContainSingle().Which.Should().Contain(nameof(OpcionesConsultas.FactorAleatorio));
        new OpcionesConsultas { FactorAleatorio = 0.5 }.Errores().Should().BeEmpty();
        new OpcionesConsultas { EsperaMinimaLimiteProveedor = TimeSpan.Zero }.Errores().Should().ContainSingle()
            .Which.Should().Contain(nameof(OpcionesConsultas.EsperaMinimaLimiteProveedor));
        new OpcionesConsultas { EsperaMinimaLimiteProveedor = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1) }.Errores().Should().ContainSingle()
            .Which.Should().Contain(nameof(OpcionesConsultas.EsperaMinimaLimiteProveedor));

        new OpcionesConsultas { UmbralPegada = TimeSpan.FromSeconds(288) }.Errores().Should().ContainSingle()
            .Which.Should().Contain(nameof(OpcionesConsultas.UmbralPegada), "4 min 48 s es la espera máxima efectiva: no la supera");
        new OpcionesConsultas { UmbralPegada = TimeSpan.FromSeconds(289) }.Errores().Should().BeEmpty();
        new OpcionesConsultas { EsperaReintento = TimeSpan.FromMinutes(2), UmbralPegada = TimeSpan.FromMinutes(9) }.Errores().Should().ContainSingle()
            .Which.Should().Contain(nameof(OpcionesConsultas.UmbralPegada));
        OpcionesConsultas.MensajeFueraDeRango.Should().Contain("Diagnostico:Consultas:FactorAleatorio").And.Contain("Diagnostico:Consultas:EsperaMinimaLimiteProveedor");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    public async Task MaxReintentos_acepta_el_rango(string valor, int esperado)
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration["Diagnostico:Consultas:MaxReintentos"] = valor);
        api.App.Services.GetRequiredService<IOptions<OpcionesConsultas>>().Value.MaxReintentos.Should().Be(esperado);
    }

    // Punto 3 AC1 — la espera y el umbral son configuración global (appsettings) y se leen al arrancar.
    [Fact]
    public async Task Espera_y_umbral_se_configuran()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: b =>
        {
            b.Configuration["Diagnostico:Consultas:EsperaReintento"] = "00:01:30";
            b.Configuration["Diagnostico:Consultas:UmbralPegada"] = "00:15:00";
            b.Configuration["Diagnostico:Consultas:FactorAleatorio"] = "0.3";
            b.Configuration["Diagnostico:Consultas:EsperaMinimaLimiteProveedor"] = "00:02:00";
        });
        var opciones = api.App.Services.GetRequiredService<IOptions<OpcionesConsultas>>().Value;
        (opciones.EsperaReintento, opciones.UmbralPegada).Should().Be((TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(15)));
        (opciones.FactorAleatorio, opciones.EsperaMinimaLimiteProveedor).Should().Be((0.3, TimeSpan.FromMinutes(2)));
    }

    // Punto 3 — una espera fuera de [1 s, 1 h], o un umbral que no supera la espera (una celda que espera su reintento contaría
    // como pegada) o pasa de 1 día, impide arrancar e indica la regla.
    [Theory]
    [InlineData("EsperaReintento", "00:00:00")]
    [InlineData("EsperaReintento", "01:00:01")]
    // HU #14752: la API de pruebas corre con MaxReintentos 1 y factor 0, así que su espera máxima efectiva es 1 min (el mínimo del
    // límite): un umbral de 1 min no la supera.
    [InlineData("UmbralPegada", "00:01:00")]
    [InlineData("UmbralPegada", "1.00:00:01")]
    [InlineData("FactorAleatorio", "0.6")]
    [InlineData("FactorAleatorio", "-0.1")]
    [InlineData("EsperaMinimaLimiteProveedor", "00:00:00")]
    [InlineData("EsperaMinimaLimiteProveedor", "00:10:01")]
    public async Task Espera_o_umbral_fuera_de_rango_impide_arrancar(string clave, string valor)
    {
        var error = await FluentActions.Awaiting(() => ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration[$"Diagnostico:Consultas:{clave}"] = valor))
            .Should().ThrowAsync<OptionsValidationException>();
        error.Which.Message.Should().Contain($"Diagnostico:Consultas:{clave}");
    }

    /// <summary>Espera del reintento n con el backoff de la política y factor 0 (configuración de las pruebas): 1, 2 y 4 min.</summary>
    private static TimeSpan PoliticaReintentosEspera(int reintento) => reintento >= 1
        ? PoliticaReintentos.EsperaPorDefecto * Math.Pow(2, reintento - 1)
        : throw new ArgumentOutOfRangeException(nameof(reintento));

    /// <summary>Intentos del lote en orden de inicio, leídos con el contexto de la empresa.</summary>
    internal static async Task<List<IntentoConsulta>> IntentosAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid diagnostico, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.DiagnosticoId == diagnostico).OrderBy(i => i.Inicio).ToListAsync(ct);
    }
}
