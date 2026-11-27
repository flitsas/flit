using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13737 (ADR-0075, Q4, U2 = a) — <see cref="ProgramadorReintentos"/>: los reintentos de las consultas por el bus los decide
/// Diagnóstico. Un transitorio con reintentos disponibles deja la celda PENDIENTE con su intento, <c>intentos_auto + 1</c> y
/// <c>disponible_desde = fin + Espera(n)</c> en un scope propio y lanza <see cref="ReintentoProgramadoException"/> (el SDK lleva el
/// resultado a la cola de reintentos con la espera fija de la política, punto 3); cuando el resultado demorado vuelve, si la espera venció se publica un
/// trabajo con <c>trabajo_id</c> nuevo y, si no, da otra vuelta. Un resultado de un trabajo que ya no es el vigente no toca la
/// celda. PostgreSQL real (<see cref="ApiDePrueba"/>), reloj manual; placas TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ProgramadorReintentosTests
/// </code>
/// </example>
public sealed class ProgramadorReintentosTests
{
    // HU #14752 (contrato-runt §3.4, alternativa A): colas .espera.n con un tick FIJO de 15 s, tantas como cubran la espera máxima
    // efectiva más 4 vueltas extra. El TTL no depende de la configuración, así que cambiar el backoff nunca da 406.
    [Fact]
    public void Las_esperas_de_la_cola_son_24_ticks_de_15_segundos_con_la_politica_por_defecto()
    {
        ProgramadorReintentos.TickCola.Should().Be(TimeSpan.FromSeconds(15));
        ProgramadorReintentos.SufijoCola.Should().Be("espera");
        var porDefecto = ProgramadorReintentos.PoliticaDe(new OpcionesConsultas());
        porDefecto.EsperaMaximaEfectiva.Should().Be(TimeSpan.FromSeconds(288));

        ProgramadorReintentos.EsperasDe(porDefecto).Should().HaveCount(24).And.OnlyContain(e => e == TimeSpan.FromSeconds(15));
        // Una base mayor solo agrega colas con el mismo TTL: ceil(2 min × 4 × 1,2 / 15 s) + 4 = 43.
        ProgramadorReintentos.EsperasDe(new PoliticaReintentos(3, TimeSpan.FromMinutes(2), 0.2, TimeSpan.FromMinutes(1)))
            .Should().HaveCount(43).And.OnlyContain(e => e == ProgramadorReintentos.TickCola);
    }

    // HU #14752 — el programador decide con el azar inyectado (AzarReintentos): con 1,0 el primer reintento va a 72 s y con 0,5 a 60 s.
    [Theory]
    [InlineData(1.0, 72)]
    [InlineData(0.5, 60)]
    [InlineData(0.0, 48)]
    public void DisponibleDesde_usa_el_azar_inyectado(double azar, int segundos)
    {
        var fin = DateTimeOffset.UnixEpoch;
        var programador = new ProgramadorReintentos(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new RelojManual(),
            Options.Create(new OpcionesConsultas()), new AzarFijo(azar), NullLogger<ProgramadorReintentos>.Instance);

        var decision = programador.Decidir("TIMEOUT", 0, fin);

        decision.Should().Be(new DecisionReintento(true, fin.AddSeconds(segundos), 1));
        programador.Decidir("LIMITE_PROVEEDOR", 0, fin).DisponibleDesde.Should().Be(fin.AddSeconds(Math.Max(segundos, 60)), "el límite espera al menos 60 s");
    }

    [Fact]
    public async Task Transitorio_con_reintentos_deja_la_celda_PENDIENTE_con_su_intento_y_lanza_la_senal()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var celda = await EnCursoAsync(api, empresa, ct, conReproceso: true);
        var fin = reloj.GetUtcNow();
        var programador = Programador(api, reloj, maxReintentos: 1);
        var decision = programador.Decidir("TIMEOUT", celda.IntentosAuto, fin);

        var programar = () => programador.ProgramarAsync(
            CeldaEnCurso.De(celda), ResultadoCelda.Error("TIMEOUT"), decision, [Intento(celda, fin, "TIMEOUT")], ct);

        await programar.Should().ThrowAsync<ReintentoProgramadoException>();
        var despues = await LeerAsync(empresa, celda.Id, ct);
        (despues.Estado, despues.IntentosAuto, despues.TipoError, despues.TrabajoId).Should().Be((EstadoCelda.Pendiente, 1, (string?)null, celda.TrabajoId));
        despues.DisponibleDesde.Should().BeCloseTo(fin + PoliticaReintentos.EsperaPorDefecto, TimeSpan.FromMilliseconds(1), "backoff desde el fin del intento (Postgres guarda microsegundos)");
        despues.ReprocesoUsuarioId.Should().BeNull("el reproceso se consumió en esta ejecución; el reintento es AUTO");
        despues.VersionTablero.Should().BeGreaterThan(celda.VersionTablero);
        var intentos = await IntentosAsync(empresa, celda.Id, ct);
        intentos.Should().ContainSingle().Which.TipoError.Should().Be("TIMEOUT", "el intento sobrevive aunque el mensaje se revierta");
    }

    [Fact]
    public async Task Resultado_de_un_trabajo_que_ya_no_es_el_vigente_no_toca_la_celda_ni_lanza()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var celda = await EnCursoAsync(api, empresa, ct);
        var programador = Programador(api, reloj, maxReintentos: 1);
        var viejo = CeldaEnCurso.De(celda) with { TrabajoId = Guid.CreateVersion7() };

        await programador.ProgramarAsync(
            viejo, ResultadoCelda.Error("RED"), programador.Decidir("RED", 0, reloj.GetUtcNow()), [Intento(celda, reloj.GetUtcNow(), "RED")], ct);

        var despues = await LeerAsync(empresa, celda.Id, ct);
        (despues.Estado, despues.IntentosAuto, despues.TrabajoId).Should().Be((EstadoCelda.EnCurso, 0, celda.TrabajoId));
        (await IntentosAsync(empresa, celda.Id, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Al_volver_antes_de_la_espera_da_otra_vuelta_y_despues_publica_un_trabajo_nuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var celda = await EnCursoAsync(api, empresa, ct);
        var programador = Programador(api, reloj, maxReintentos: 2);
        // Segundo reintento (intentos_auto = 1). HU #14752: backoff 1/2/4 min (azar 0,5 = sin desvío): el segundo espera 2 min; el
        // resultado que vuelve a mitad de la espera da otra vuelta y el que vuelve cumplida la espera publica.
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.consultas SET intentos_auto = 1 WHERE id = '{celda.Id}'", ct);
        var decision = programador.Decidir("TIMEOUT", 1, reloj.GetUtcNow());
        var espera = programador.Politica.Espera * 2;
        programador.Politica.Espera.Should().Be(PoliticaReintentos.EsperaPorDefecto);
        decision.DisponibleDesde.Should().Be(reloj.GetUtcNow() + espera, "el segundo reintento espera el doble de la base");
        await programador.Invoking(p => p.ProgramarAsync(CeldaEnCurso.De(celda), ResultadoCelda.Error("TIMEOUT"), decision, [], ct))
            .Should().ThrowAsync<ReintentoProgramadoException>();

        var mitad = espera / 2;
        reloj.Avanzar(mitad);
        (await ReanudarAsync(api, empresa, celda.Id, reloj, ct)).Should().BeTrue("a mitad de la espera no vence: otra vuelta");
        (await LeerAsync(empresa, celda.Id, ct)).TrabajoId.Should().Be(celda.TrabajoId);

        reloj.Avanzar(espera - mitad);
        (await ReanudarAsync(api, empresa, celda.Id, reloj, ct)).Should().BeFalse("vencida la espera, se publica");
        var publicada = await LeerAsync(empresa, celda.Id, ct);
        publicada.TrabajoId.Should().NotBeNull().And.NotBe(celda.TrabajoId!.Value, "el reintento es un trabajo nuevo");
        publicada.Estado.Should().Be(EstadoCelda.EnCurso, "el trabajo nuevo está en vuelo");
        publicada.IntentosAuto.Should().Be(2);
        var trabajos = (await OutboxDePrueba.TrabajosAsync(api, ct)).Where(t => t.ConsultaId == celda.Id).ToList();
        trabajos.Select(t => t.TrabajoId).Should().Equal(celda.TrabajoId!.Value, publicada.TrabajoId!.Value);
    }

    private static ProgramadorReintentos Programador(ApiDePrueba api, TimeProvider reloj, int maxReintentos) => new(
        api.App.Services.GetRequiredService<IServiceScopeFactory>(), reloj, Options.Create(new OpcionesConsultas { MaxReintentos = maxReintentos }),
        new AzarFijo(0.5), NullLogger<ProgramadorReintentos>.Instance);

    /// <summary>Azar fijo para que <c>DisponibleDesde</c> sea determinista (0,5 = sin desvío).</summary>
    private sealed class AzarFijo(double valor) : AzarReintentos
    {
        public override double Siguiente() => valor;
    }

    /// <summary>Lote de un vehículo con su RUNT publicada y en vuelo (EN_CURSO, como la deja el publicador del bus).</summary>
    private static async Task<CeldaConsulta> EnCursoAsync(ApiDePrueba api, EmpresaDePrueba empresa, CancellationToken ct, bool conReproceso = false)
    {
        var lote = await Motor.LoteConfirmadoAsync(api, empresa, [Lotes.Fila("TST701")], ct);
        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        celda.TrabajoId.Should().NotBeNull("la RUNT se publica al encolar");
        var reproceso = conReproceso ? $", reproceso_usuario_id = '{Guid.NewGuid()}'" : string.Empty;
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.consultas SET estado = 'EN_CURSO'{reproceso} WHERE id = '{celda.Id}'", ct);
        return await LeerAsync(empresa, celda.Id, ct);
    }

    /// <summary>Lo que hace el consumidor con el resultado demorado; true si lanzó la señal (otra vuelta).</summary>
    private static async Task<bool> ReanudarAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid celdaId, TimeProvider reloj, CancellationToken ct)
    {
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celdaId, ct);
        try
        {
            await ProgramadorReintentos.ReanudarAsync(celda, reloj.GetUtcNow(), scope.ServiceProvider.GetRequiredService<PublicadorCeldas>(), ct);
        }
        catch (ReintentoProgramadoException)
        {
            return true;
        }

        await db.SaveChangesAsync(ct);
        return false;
    }

    private static IntentoConsulta Intento(CeldaConsulta celda, DateTimeOffset fin, string tipoError) => new()
    {
        TenantId = celda.TenantId,
        DiagnosticoId = celda.DiagnosticoId,
        VehiculoId = celda.VehiculoId,
        ConsultaId = celda.Id,
        Fuente = celda.Fuente,
        IdentificadorConsultado = "TST701",
        Disparador = DisparadorIntento.Auto,
        Inicio = fin.AddMilliseconds(-800),
        Fin = fin,
        Estado = ResultadoIntento.Error,
        TipoError = tipoError,
        MensajeError = "core-consultas respondió DeadlineExceeded.",
    };

    private static async Task<CeldaConsulta> LeerAsync(EmpresaDePrueba empresa, Guid celda, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.Id == celda, ct);
    }

    private static async Task<List<IntentoConsulta>> IntentosAsync(EmpresaDePrueba empresa, Guid celda, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<IntentoConsulta>().AsNoTracking().Where(i => i.ConsultaId == celda).ToListAsync(ct);
    }
}
