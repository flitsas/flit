using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075, T11, D3, 6r-i, U4 = a) — <see cref="ReconciliadorConsultas"/>: una celda en curso sin resultado tras el
/// margen (30 min) se republica con un <c>trabajo_id</c> nuevo (<c>republicaciones</c> = 1) y, si vuelve a quedar huérfana, se
/// cierra en ERROR (reprocesable), con el bloqueo de las demás si es la RUNT. Si core-consultas no devolvió ningún resultado en la
/// ventana de vida (10 min), los trabajos están esperando en su cola: no republica ni cierra. Las celdas que nunca se publicaron
/// (de antes del corte) se publican por primera vez aunque consultas esté mudo. Busca entre empresas y trabaja cada celda con la
/// suya. PostgreSQL real, reloj manual y el bus de prueba (los trabajos «perdidos» se descartan sin responder).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReconciliadorTests
/// </code>
/// </example>
public sealed class ReconciliadorTests
{
    [Fact]
    public void Las_opciones_por_defecto_son_las_del_diseno_y_se_validan()
    {
        var o = new OpcionesReconciliador();
        (o.Intervalo, o.Margen, o.VentanaVida).Should().Be((TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)));
        o.Errores().Should().BeEmpty();
        new OpcionesReconciliador { Intervalo = TimeSpan.Zero, VentanaVida = TimeSpan.Zero, Margen = TimeSpan.FromSeconds(5), Maximo = 0 }.Errores().Should().HaveCount(4);
    }

    [Fact]
    public void El_pulso_dice_si_llego_algun_resultado_en_la_ventana()
    {
        var pulso = new PulsoConsultas();
        var ahora = DateTimeOffset.UtcNow;
        pulso.UltimoResultado.Should().BeNull();
        pulso.Vivo(ahora, TimeSpan.FromMinutes(10)).Should().BeFalse("sin ningún resultado desde que arrancó, en pausa");

        pulso.Registrar(ahora.AddMinutes(-11));
        pulso.Vivo(ahora, TimeSpan.FromMinutes(10)).Should().BeFalse();
        pulso.Registrar(ahora.AddMinutes(-2));
        pulso.Registrar(ahora.AddMinutes(-20));
        pulso.UltimoResultado.Should().Be(ahora.AddMinutes(-2), "se queda con el más reciente");
        pulso.Vivo(ahora, TimeSpan.FromMinutes(10)).Should().BeTrue();
    }

    // 6r-i: huérfana con consultas vivo → republica una vez (trabajo_id nuevo); huérfana otra vez → ERROR y bloquea las demás.
    // Dos empresas: cada celda se reconcilia con la suya.
    [Fact]
    public async Task Huerfana_se_republica_una_vez_y_a_la_siguiente_cierra_en_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var loteA = await MotorRunt.LoteAsync(api, empresas.A, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var loteB = await MotorRunt.LoteAsync(api, empresas.B, [Lotes.Fila("TST002")], ct);
        var bus = Motor.Bus(api);
        var perdidos = await bus.DescartarTrabajosAsync(ct);
        perdidos.Should().HaveCount(2, "los trabajos de las dos RUNT se pierden");
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();

        reloj.Avanzar(TimeSpan.FromMinutes(10));
        pulso.Registrar(reloj.GetUtcNow());
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(0, 0, 0, 0, 0), "antes del margen nadie es huérfana");

        reloj.Avanzar(TimeSpan.FromMinutes(21));
        pulso.Registrar(reloj.GetUtcNow());
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(2, 0, 2, 0, 0));

        var runtA = (await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct)).Single(c => c.Fuente == Fuente.Runt);
        var runtB = (await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct)).Single();
        (runtA.Estado, runtA.Republicaciones).Should().Be((EstadoCelda.EnCurso, (short)1));
        (runtB.Estado, runtB.Republicaciones).Should().Be((EstadoCelda.EnCurso, (short)1));
        var republicados = await bus.DescartarTrabajosAsync(ct);
        republicados.Select(t => (t.ConsultaId, t.TrabajoId, t.Sobre.TenantId)).Should().BeEquivalentTo(
            [(runtA.Id, runtA.TrabajoId!.Value, empresas.A.Id), (runtB.Id, runtB.TrabajoId!.Value, empresas.B.Id)], "cada trabajo nuevo con la empresa de su celda");
        perdidos.Select(t => t.TrabajoId).Should().NotIntersectWith(republicados.Select(t => t.TrabajoId));

        reloj.Avanzar(TimeSpan.FromMinutes(31));
        pulso.Registrar(reloj.GetUtcNow());
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(2, 0, 0, 2, 0));

        var celdasA = await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct);
        celdasA.Single(c => c.Fuente == Fuente.Runt).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = (string?)null });
        celdasA.Single(c => c.Fuente == Fuente.SimitVehiculo).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = OrquestadorVehiculo.BloqueadaPorRunt });
        (await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct)).Single().Estado.Should().Be(EstadoCelda.Error);
        var intento = (await MotorPersonas.IntentosAsync(empresas.A, runtA.Id, ct)).Should().ContainSingle("el trabajo republicado que se perdió queda auditado").Subject;
        (intento.Estado, intento.TipoError).Should().Be((ResultadoIntento.Error, (string?)null));
        (await bus.DescartarTrabajosAsync(ct)).Should().BeEmpty("cerrar no publica nada");
    }

    // U4 = a: sin resultados de core-consultas en la ventana de vida, las huérfanas esperan; al volver los resultados, se republican.
    [Fact]
    public async Task Sin_resultados_de_consultas_en_la_ventana_no_republica_ni_cierra()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var perdido = (await bus.DescartarTrabajosAsync(ct)).Single();
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();
        pulso.Registrar(reloj.GetUtcNow());

        reloj.Avanzar(TimeSpan.FromHours(3));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 0, 0, 0, 1), "consultas mudo hace más de 10 min: en pausa");
        (await reconciliador.ReconciliarAsync(ct)).EnPausa.Should().Be(1, "ni a la segunda vuelta cierra en ERROR");
        var celda = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single();
        (celda.Estado, celda.TrabajoId, celda.Republicaciones).Should().Be((EstadoCelda.EnCurso, perdido.TrabajoId, (short)0));
        (await bus.DescartarTrabajosAsync(ct)).Should().BeEmpty("no duplica trabajos mientras consultas está caído");

        // Vuelve a fluir un resultado (el de otro lote, por ejemplo): la huérfana se republica.
        pulso.Registrar(reloj.GetUtcNow());
        (await reconciliador.ReconciliarAsync(ct)).Republicadas.Should().Be(1);
        await bus.ProcesarTodoAsync(ct);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
    }

    // 6r-i: una celda que nunca se publicó (PENDIENTE no diferida sin trabajo_id, de antes del corte) se publica por primera vez,
    // aunque consultas esté mudo: publicarla no duplica nada.
    [Fact]
    public async Task Celda_sin_trabajo_de_antes_del_corte_se_publica_por_primera_vez()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api);
        await bus.DescartarTrabajosAsync(ct);
        var runt = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single(c => c.Fuente == Fuente.Runt);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.consultas SET estado = 'PENDIENTE', trabajo_id = NULL WHERE id = '{runt.Id}'", ct);
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();

        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 1, 0, 0, 0), "la diferida (SIMIT) espera a su RUNT");

        var publicada = (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single(c => c.Fuente == Fuente.Runt);
        (publicada.Estado, publicada.Republicaciones).Should().Be((EstadoCelda.EnCurso, (short)0));
        (await bus.DescartarTrabajosAsync(ct)).Should().ContainSingle().Which.TrabajoId.Should().Be(publicada.TrabajoId!.Value);
    }

    // Un resultado tardío del trabajo anterior a la republicación se descarta; el del trabajo nuevo cierra la celda.
    [Fact]
    public async Task Tras_republicar_el_resultado_tardio_del_trabajo_viejo_se_descarta()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var viejo = (await bus.DescartarTrabajosAsync(ct)).Single();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();
        reloj.Avanzar(TimeSpan.FromMinutes(31));
        pulso.Registrar(reloj.GetUtcNow());
        (await api.App.Services.GetRequiredService<ReconciliadorConsultas>().ReconciliarAsync(ct)).Republicadas.Should().Be(1);

        (await bus.EntregarAsync(bus.Resultado(viejo, await ConsultasSimuladas.ResponderAsync(api, viejo, ct)), ct)).Should().BeTrue();
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.EnCurso, "el resultado tardío no es del trabajo vigente");

        await bus.ProcesarTodoAsync(ct);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
    }
}
