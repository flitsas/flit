using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Bus;
using Flit.Diagnostico.Tests.Configuracion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075, T11, D3, 6r-i, U4 = a, Dec. 6) — <see cref="ReconciliadorConsultas"/>. Una celda en curso solo cuenta
/// como perdida si, pasado el margen (30 min), core-consultas ya devolvió resultados de trabajos publicados DESPUÉS que el suyo
/// en la misma cola (lote o Express) más allá de la tolerancia de orden (15 min): la marca de agua FIFO de
/// <see cref="PulsoConsultas"/>. Entonces se republica con un <c>trabajo_id</c> nuevo (<c>republicaciones</c> = 1) y, si vuelve a
/// quedar perdida, se cierra en ERROR (reprocesable), con el bloqueo de las demás si es la RUNT. Un lote grande que espera su
/// turno con consultas vivo no se toca. Si core-consultas no devolvió ningún resultado en la ventana de vida (10 min), no
/// republica ni cierra (pausa). Pasados TTL (24 h) + margen desde la publicación, el mensaje ya venció en el broker: la celda se
/// cierra en ERROR reprocesable aunque consultas esté mudo. Las celdas que nunca se publicaron (de antes del corte) se publican
/// por primera vez. Busca entre empresas y trabaja cada celda con la suya. PostgreSQL real, reloj manual y el bus de prueba (los
/// trabajos «perdidos» se descartan sin responder; los demás pasan por el consumidor de resultados real).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReconciliadorTests
/// </code>
/// </example>
public sealed class ReconciliadorTests
{
    private static readonly string[] ColasPrincipales = [BrokerDePrueba.ColaLote, BrokerDePrueba.ColaExpress, BrokerDePrueba.ColaResultados];

    [Fact]
    public void Las_opciones_por_defecto_son_las_del_diseno_y_se_validan()
    {
        var o = new OpcionesReconciliador();
        (o.Intervalo, o.Margen, o.VentanaVida).Should().Be((TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)));
        (o.ToleranciaOrden, o.TtlTrabajo).Should().Be((TimeSpan.FromMinutes(15), TimeSpan.FromHours(24)));
        o.Errores().Should().BeEmpty();
        new OpcionesReconciliador { Intervalo = TimeSpan.Zero, VentanaVida = TimeSpan.Zero, Margen = TimeSpan.FromSeconds(5), Maximo = 0 }.Errores().Should().HaveCount(4);
        new OpcionesReconciliador { TtlTrabajo = TimeSpan.FromMinutes(30) }.Errores().Should().ContainSingle()
            .Which.Should().Contain("TtlTrabajo", "el TTL debe ser mayor que el margen: el cierre por vencimiento se mide con los dos");
        new OpcionesReconciliador { ToleranciaOrden = TimeSpan.FromSeconds(30) }.Errores().Should().ContainSingle().Which.Should().Contain("ToleranciaOrden");
        new OpcionesReconciliador { ToleranciaOrden = TimeSpan.FromHours(24) }.Errores().Should().ContainSingle()
            .Which.Should().Contain("ToleranciaOrden", "una tolerancia del tamaño del TTL nunca detectaría una pérdida antes del vencimiento");
    }

    // Una sola fuente de verdad: el TTL de las tres colas principales vive en definitions.json y la opción por defecto coincide.
    // Lo vencido sale por el exchange de reintentos de la cola (el que declara el SDK) hacia su .dlq, que purga dlq-7d.
    [Fact]
    public void El_TTL_por_defecto_es_el_de_la_politica_del_broker_y_lo_vencido_va_a_la_DLQ()
    {
        var ttl = (long)new OpcionesReconciliador().TtlTrabajo.TotalMilliseconds;
        foreach (var cola in ColasPrincipales)
        {
            DefinicionesBroker.PoliticasQueAlcanzan(cola).Should().ContainSingle($"RabbitMQ aplica una sola política a {cola}: no debe chocar con otra");
            var politica = DefinicionesBroker.PoliticaDe(cola)!;
            politica.TtlMs.Should().Be(ttl, $"el TTL de {cola} y Diagnostico:Consultas:Reconciliador:TtlTrabajo deben coincidir");
            (politica.Texto("dead-letter-exchange"), politica.Texto("dead-letter-routing-key")).Should().Be(($"{cola}.reintentos", $"{cola}.dlq"));
            DefinicionesBroker.PoliticaDe($"{cola}.dlq")!.Nombre.Should().Be("dlq-7d", "la DLQ conserva su purga de 7 días (Ley 1581)");
            DefinicionesBroker.PoliticasQueAlcanzan($"{cola}.retry.1").Should().BeEmpty("las colas de reintento ya tienen su propio TTL");
        }
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

    // Dec. 6: la marca de agua es el instante de publicación (UUIDv7 del trabajo_id) del trabajo más reciente que devolvió
    // core-consultas, por cola, monótona; un id que no es v7 no la mueve.
    [Fact]
    public void La_marca_de_agua_es_por_cola_monotona_y_sale_del_trabajo_id()
    {
        var pulso = new PulsoConsultas();
        var t0 = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        pulso.UltimoAtendido(esExpress: false).Should().BeNull("una réplica sin datos queda del lado seguro");

        pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(t0.AddMinutes(5)));
        pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(t0));
        pulso.RegistrarAtendido(esExpress: false, Guid.NewGuid());

        pulso.UltimoAtendido(esExpress: false).Should().Be(t0.AddMinutes(5));
        pulso.UltimoAtendido(esExpress: true).Should().BeNull("la del Express es aparte");
        PulsoConsultas.PublicadoEn(Guid.CreateVersion7(t0)).Should().Be(t0);
        PulsoConsultas.PublicadoEn(Guid.NewGuid()).Should().BeNull();
    }

    // Dec. 6: 30.000 trabajos con concurrencia 8 esperan horas su turno con consultas vivo. Sin resultados de trabajos publicados
    // después del suyo (o solo dentro de la tolerancia), ninguna celda está perdida: ni se republica ni se cierra.
    [Fact]
    public async Task Lote_grande_en_espera_con_consultas_vivo_no_republica_ni_cierra()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var publicado = reloj.GetUtcNow();
        var lote = await MotorRunt.LoteAsync(api, empresa, Motor.Placas(6, desde: 21), ct);
        var bus = Motor.Bus(api);
        var enCola = await bus.DescartarTrabajosAsync(ct);
        enCola.Should().HaveCount(6, "esperan en consultas.diagnostico");
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();

        foreach (var horas in new[] { 1, 3, 6 })
        {
            reloj.Avanzar(TimeSpan.FromHours(horas));
            pulso.Registrar(reloj.GetUtcNow());
            // consultas atiende trabajos de antes y del mismo instante (concurrencia), nunca más allá de la tolerancia.
            pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(publicado.AddMinutes(10)));
            (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(0, 0, 0, 0, 0, 0), $"a las {horas} h más siguen esperando su turno");
        }

        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Should().OnlyContain(c => c.Estado == EstadoCelda.EnCurso && c.Republicaciones == 0)
            .And.Subject.Select(c => c.TrabajoId!.Value).Should().BeEquivalentTo(enCola.Select(t => t.TrabajoId));
        (await bus.DescartarTrabajosAsync(ct)).Should().BeEmpty("no duplica ninguna consulta");
    }

    // 6r-i + Dec. 6: con resultados de trabajos publicados después (consultas los devolvió por el consumidor real) la celda está
    // perdida → republica una vez (trabajo_id nuevo); perdida otra vez → ERROR y bloquea las demás. Dos empresas: cada celda con la suya.
    [Fact]
    public async Task Con_resultados_de_trabajos_posteriores_se_republica_una_vez_y_a_la_siguiente_cierra_en_ERROR()
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
        await OtroLoteAtendidoAsync(api, bus, empresas.A, "TST031", ct);
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(0, 0, 0, 0, 0, 0),
            "antes del margen nadie es huérfana, y un posterior dentro de la tolerancia (10 min) no prueba la pérdida");

        reloj.Avanzar(TimeSpan.FromMinutes(10));
        await OtroLoteAtendidoAsync(api, bus, empresas.B, "TST032", ct);
        reloj.Avanzar(TimeSpan.FromMinutes(11));
        pulso.Registrar(reloj.GetUtcNow());
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(2, 0, 2, 0, 0, 0),
            "consultas devolvió un trabajo publicado 20 min después de los perdidos, en su misma cola");

        var runtA = (await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct)).Single(c => c.Fuente == Fuente.Runt);
        var runtB = (await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct)).Single();
        (runtA.Estado, runtA.Republicaciones).Should().Be((EstadoCelda.EnCurso, (short)1));
        (runtB.Estado, runtB.Republicaciones).Should().Be((EstadoCelda.EnCurso, (short)1));
        var republicados = await bus.DescartarTrabajosAsync(ct);
        republicados.Select(t => (t.ConsultaId, t.TrabajoId, t.Sobre.TenantId)).Should().BeEquivalentTo(
            [(runtA.Id, runtA.TrabajoId!.Value, empresas.A.Id), (runtB.Id, runtB.TrabajoId!.Value, empresas.B.Id)], "cada trabajo nuevo con la empresa de su celda");
        perdidos.Select(t => t.TrabajoId).Should().NotIntersectWith(republicados.Select(t => t.TrabajoId));

        reloj.Avanzar(TimeSpan.FromMinutes(31));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(0, 0, 0, 0, 0, 0),
            "los republicados son posteriores a todo lo que devolvió consultas: esperan su turno");
        await OtroLoteAtendidoAsync(api, bus, empresas.B, "TST033", ct);
        reloj.Avanzar(TimeSpan.FromMinutes(1));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(2, 0, 0, 2, 0, 0));

        var celdasA = await Motor.CeldasAsync(api, empresas.A.Id, loteA, ct);
        celdasA.Single(c => c.Fuente == Fuente.Runt).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = (string?)null });
        celdasA.Single(c => c.Fuente == Fuente.SimitVehiculo).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = OrquestadorVehiculo.BloqueadaPorRunt });
        (await Motor.CeldasAsync(api, empresas.B.Id, loteB, ct)).Single().Estado.Should().Be(EstadoCelda.Error);
        var intento = (await MotorPersonas.IntentosAsync(empresas.A, runtA.Id, ct)).Should().ContainSingle("el trabajo republicado que se perdió queda auditado").Subject;
        (intento.Estado, intento.TipoError, intento.MensajeError).Should().Be((ResultadoIntento.Error, (string?)null, ReconciliadorConsultas.MensajeSinResultado));
        (await bus.DescartarTrabajosAsync(ct)).Should().BeEmpty("cerrar no publica nada");
    }

    // Dec. 6: las marcas de agua de lote y Express son independientes: lo que devuelve una cola no prueba nada de la otra.
    [Fact]
    public async Task La_marca_de_agua_de_Express_no_afecta_al_lote_y_viceversa()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var publicado = reloj.GetUtcNow();
        var lote = await MotorRunt.LoteAsync(api, empresas.A, [Lotes.Fila("TST001")], ct);
        var express = await MotorRunt.LoteAsync(api, empresas.B, [Lotes.Fila("TST002")], ct);
        await Motor.EjecutarAsync(api, $"UPDATE diagnostico.diagnosticos SET es_express = true WHERE id = '{express}'", ct);
        var bus = Motor.Bus(api);
        (await bus.DescartarTrabajosAsync(ct)).Should().HaveCount(2);
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();

        reloj.Avanzar(TimeSpan.FromMinutes(31));
        pulso.Registrar(reloj.GetUtcNow());
        pulso.RegistrarAtendido(esExpress: true, Guid.CreateVersion7(publicado.AddMinutes(20)));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 0, 1, 0, 0, 0), "solo la del Express está perdida");
        (await Motor.CeldasAsync(api, empresas.B.Id, express, ct)).Single().Republicaciones.Should().Be(1);
        (await Motor.CeldasAsync(api, empresas.A.Id, lote, ct)).Single().Republicaciones.Should().Be(0, "el lote espera su turno en su cola");
        (await bus.DescartarTrabajosAsync(ct)).Should().ContainSingle().Which.RoutingKey.Should().StartWith("consultas.diagnostico-express.");

        pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(publicado.AddMinutes(20)));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 0, 1, 0, 0, 0),
            "ahora la del lote; la del Express se republicó a los 31 min, después de la marca del Express");
        (await Motor.CeldasAsync(api, empresas.A.Id, lote, ct)).Single().Republicaciones.Should().Be(1);
        (await Motor.CeldasAsync(api, empresas.B.Id, express, ct)).Single().Estado.Should().Be(EstadoCelda.EnCurso);
    }

    // U4 = a: perdida según la marca, pero sin resultados de core-consultas en la ventana de vida: espera; al volver, se republica.
    [Fact]
    public async Task Sin_resultados_de_consultas_en_la_ventana_no_republica_ni_cierra()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var publicado = reloj.GetUtcNow();
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var perdido = (await bus.DescartarTrabajosAsync(ct)).Single();
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();
        reloj.Avanzar(TimeSpan.FromMinutes(20));
        pulso.Registrar(reloj.GetUtcNow());
        pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(publicado.AddMinutes(20)));

        reloj.Avanzar(TimeSpan.FromHours(3));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 0, 0, 0, 1, 0), "consultas mudo hace más de 10 min: en pausa");
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

    // Dec. 6 (TTL): pasados TTL (24 h) + margen desde la publicación, el mensaje ya venció en el broker (salió a la DLQ) y no puede
    // duplicarse: la celda cierra en ERROR reprocesable aunque consultas esté mudo y sin marca, con el bloqueo de las demás (RUNT).
    [Fact]
    public async Task Vencido_el_TTL_mas_el_margen_cierra_en_ERROR_reprocesable_aunque_consultas_este_mudo()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await MotorRunt.ArrancarAsync(b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var bus = Motor.Bus(api);
        (await bus.DescartarTrabajosAsync(ct)).Should().ContainSingle();
        var reconciliador = api.App.Services.GetRequiredService<ReconciliadorConsultas>();

        reloj.Avanzar(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(29));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(0, 0, 0, 0, 0, 0), "antes de TTL + margen, nada");

        reloj.Avanzar(TimeSpan.FromMinutes(2));
        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 0, 0, 0, 0, 1));

        var celdas = await Motor.CeldasAsync(api, empresa.Id, lote, ct);
        var runt = celdas.Single(c => c.Fuente == Fuente.Runt);
        runt.Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = (string?)null, Republicaciones = (short)0 });
        celdas.Single(c => c.Fuente == Fuente.SimitVehiculo).Should().BeEquivalentTo(new { Estado = EstadoCelda.Error, TipoError = OrquestadorVehiculo.BloqueadaPorRunt });
        var intento = (await MotorPersonas.IntentosAsync(empresa, runt.Id, ct)).Should().ContainSingle().Subject;
        (intento.Estado, intento.MensajeError).Should().Be((ResultadoIntento.Error, ReconciliadorConsultas.MensajeVencido));
        (await bus.DescartarTrabajosAsync(ct)).Should().BeEmpty("cerrar no publica nada");

        // Reprocesable: el reproceso de la RUNT la vuelve a publicar.
        var reproceso = await api.Cliente(MotorPersonas.Admin(api, empresa)).PostAsync($"{Program.Prefijo}/consultas/{runt.Id}/reprocesar", null, ct);
        reproceso.IsSuccessStatusCode.Should().BeTrue(await reproceso.Content.ReadAsStringAsync(ct));
        (await bus.DescartarTrabajosAsync(ct)).Should().ContainSingle().Which.ConsultaId.Should().Be(runt.Id);
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

        (await reconciliador.ReconciliarAsync(ct)).Should().Be(new VueltaReconciliador(1, 1, 0, 0, 0, 0), "la diferida (SIMIT) espera a su RUNT");

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
        var publicado = reloj.GetUtcNow();
        var lote = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var bus = Motor.Bus(api);
        var viejo = (await bus.DescartarTrabajosAsync(ct)).Single();
        var pulso = api.App.Services.GetRequiredService<PulsoConsultas>();
        reloj.Avanzar(TimeSpan.FromMinutes(31));
        pulso.Registrar(reloj.GetUtcNow());
        pulso.RegistrarAtendido(esExpress: false, Guid.CreateVersion7(publicado.AddMinutes(20)));
        (await api.App.Services.GetRequiredService<ReconciliadorConsultas>().ReconciliarAsync(ct)).Republicadas.Should().Be(1);

        (await bus.EntregarAsync(bus.Resultado(viejo, await ConsultasSimuladas.ResponderAsync(api, viejo, ct)), ct)).Should().BeTrue();
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.EnCurso, "el resultado tardío no es del trabajo vigente");

        await bus.ProcesarTodoAsync(ct);
        (await Motor.CeldasAsync(api, empresa.Id, lote, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
    }

    /// <summary>
    /// Otro lote de <paramref name="empresa"/> (configuración ya sembrada) con la RUNT de <paramref name="placa"/>, publicado ahora y
    /// atendido por el bus: su resultado pasa por el consumidor real, que mueve el pulso y la marca de agua del lote.
    /// </summary>
    private static async Task OtroLoteAtendidoAsync(ApiDePrueba api, BusDePrueba bus, EmpresaDePrueba empresa, string placa, CancellationToken ct)
    {
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote posterior", "SIMPLE", null, ["RUNT"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var confirmada = await Lotes.ConfirmarAsync(cliente, id, [Lotes.Fila(placa)], ct);
        confirmada.StatusCode.Should().Be(HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        await bus.ProcesarTodoAsync(ct);
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
    }
}
