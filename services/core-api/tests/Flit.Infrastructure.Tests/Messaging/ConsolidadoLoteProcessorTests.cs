using System.Collections.Concurrent;
using Flit.Infrastructure.Messaging;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #13376 (Épica #13216, ADR-0070 D2) — <see cref="ConsolidadoLoteProcessor"/> sin base de datos: el reclamo y el
/// cierre son dobles en memoria y el <see cref="ProcesarItemLoteHandler"/> es el real. Cubre el tope de slots, los
/// parámetros incoherentes, el timeout, el scope por ítem con su tenant, el motor apagado y los intentos agotados.
/// <para>Uso de ejemplo:
/// <code>
/// var (processor, repo, proceso, origen) = Crear(settings);
/// await processor.StartAsync(ct); // reclama de repo.Pendientes y cierra con proceso
/// </code></para>
/// </summary>
public sealed class ConsolidadoLoteProcessorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConsolidadoExportSettings Settings(
        bool activo = true, short slots = 2, int timeout = 300, int lease = 600, short maxIntentos = 3) => new()
        {
            IsActive = activo,
            ItemSlots = slots,
            ItemTimeoutSeconds = timeout,
            ItemLeaseSeconds = lease,
            MaxItemAttempts = maxIntentos,
            RetryDelaySeconds = 30,
        };

    private static ItemLoteReclamado Reclamado(Guid? tenantItem = null, short attempts = 0)
    {
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            RequestedByUserId = Guid.NewGuid(),
            RequestedRoleCode = "Radicador",
            Origin = ConsolidadoExportOrigin.Tramites,
            DocumentType = ConsolidadoExportDocumentType.Consolidado,
            Status = ConsolidadoExportStatus.EnProceso,
        };
        var item = new ConsolidadoExportBatchItem
        {
            Id = Guid.NewGuid(),
            BatchId = lote.Id,
            TenantId = tenantItem ?? lote.TenantId!.Value,
            ProcedureInstanceId = Guid.NewGuid(),
            Status = ConsolidadoExportItemStatus.Procesando,
            Attempts = attempts,
            LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        return new ItemLoteReclamado(lote, item);
    }

    private static async Task EsperarAsync(Func<bool> condicion, string porque)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < limite && !condicion())
            await Task.Delay(20, Ct);
        condicion().Should().BeTrue(porque);
    }

    private static (ConsolidadoLoteProcessor Processor, FakeRepo Repo, FakeProceso Proceso, FakeOrigen Origen, ListLogger Log) Crear(
        ConsolidadoExportSettings? settings, params ItemLoteReclamado[] pendientes)
    {
        var repo = new FakeRepo { Settings = settings };
        foreach (var p in pendientes)
            repo.Pendientes.Enqueue(p);
        var proceso = new FakeProceso();
        var origen = new FakeOrigen();
        var log = new ListLogger();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConsolidadoLoteRepository>(repo);
        services.AddSingleton<IConsolidadoLoteItemProceso>(proceso);
        services.AddScoped<MarcaDeScope>();
        services.AddScoped<ILoteItemOrigen>(sp => new OrigenEnScope(origen, sp.GetRequiredService<MarcaDeScope>()));
        services.AddScoped<LoteItemOrigenPorOrigen>();
        services.AddScoped(sp => new ProcesarItemLoteHandler(
            sp.GetRequiredService<LoteItemOrigenPorOrigen>(),
            sp.GetRequiredService<IConsolidadoLoteItemProceso>(),
            sp.GetRequiredService<ILogger<ProcesarItemLoteHandler>>()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var processor = new ConsolidadoLoteProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            log,
            new ConsolidadoLoteProcessorOptions(TimeSpan.FromMilliseconds(30), TimeSpan.Zero),
            TimeProvider.System);
        return (processor, repo, proceso, origen, log);
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_RespetaItemSlots_YCierraTodos()
    {
        var items = Enumerable.Range(0, 6).Select(_ => Reclamado()).ToArray();
        var (processor, repo, proceso, origen, _) = Crear(Settings(slots: 2), items);
        origen.Retardo = TimeSpan.FromMilliseconds(120);

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Incluidos.Count == 6, "los 6 ítems se cierran");
        await processor.StopAsync(Ct);

        origen.MaxConcurrencia.Should().Be(2, "item_slots = 2 se alcanza y nunca se supera");
        repo.LeasesPedidos.Should().AllSatisfy(l => l.Should().Be(600), "el lease del reclamo es item_lease_seconds");
    }

    // ── AC4 ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(300, 600, true)]
    [InlineData(300, 300, false)]
    [InlineData(600, 300, false)]
    public void AC4_ParametrosValidos_ExigeLeaseMayorQueTimeout(int timeout, int lease, bool esperado) =>
        ConsolidadoLoteProcessor.ParametrosValidos(Settings(timeout: timeout, lease: lease)).Should().Be(esperado);

    [Fact]
    public async Task AC4_ConLeaseNoMayorQueTimeout_ElCarrilNoArranca_YLoRegistra()
    {
        var (processor, repo, _, origen, log) = Crear(Settings(timeout: 300, lease: 300), Reclamado());

        await processor.StartAsync(Ct);
        await EsperarAsync(() => log.Entradas.Any(e => e.Nivel == LogLevel.Error), "registra el error de parámetros");
        await Task.Delay(150, Ct);
        await processor.StopAsync(Ct);

        repo.Reclamos.Should().Be(0, "el carril no arranca");
        origen.Llamadas.Should().BeEmpty();
        log.Entradas.Where(e => e.Nivel == LogLevel.Error).Should().ContainSingle("se registra una vez, no en cada ciclo")
            .Which.Mensaje.Should().Contain("300");
    }

    [Fact]
    public async Task AC4_ElTimeoutCancelaLaEjecucion_YLaCuentaComoFalloTecnico()
    {
        var item = Reclamado();
        var (processor, _, proceso, origen, _) = Crear(Settings(timeout: 1, lease: 2), item);
        origen.Bloquear = true;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Reprogramados.Count == 1, "el ítem se reprograma tras el timeout");
        await processor.StopAsync(Ct);

        origen.Cancelaciones.Should().Be(1, "la ejecución se cancela al vencer item_timeout_seconds");
        var r = proceso.Reprogramados.Single();
        r.ItemId.Should().Be(item.Item.Id);
        r.Intentos.Should().Be(1, "attempts + 1, como cualquier fallo técnico");
    }

    [Fact]
    public async Task AC4_TimeoutEnElUltimoIntento_OmiteConErrorTecnico()
    {
        var (processor, _, proceso, origen, _) = Crear(Settings(timeout: 1, lease: 2, maxIntentos: 2), Reclamado(attempts: 1));
        origen.Bloquear = true;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Omitidos.Count == 1, "agotó los intentos");
        await processor.StopAsync(Ct);

        proceso.Omitidos.Single().Codigo.Should().Be(ConsolidadoLoteOmisiones.ErrorTecnico);
        proceso.Omitidos.Single().Intentos.Should().Be(2);
    }

    // ── AC5 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_CadaItem_CorreEnUnScopeNuevo_ConElTenantDelItem()
    {
        var tenants = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var items = tenants.Select(t => Reclamado(t)).ToArray();
        var (processor, _, proceso, origen, _) = Crear(Settings(slots: 1), items);

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Incluidos.Count == 3, "los 3 ítems se cierran");
        await processor.StopAsync(Ct);

        origen.Llamadas.Select(l => l.Scope).Distinct().Should().HaveCount(3, "un scope de DI por ítem");
        origen.Llamadas.Select(l => l.Contexto.CompaniaTramiteId).Should().BeEquivalentTo(tenants, "el tenant del ítem viaja al contexto");
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_CierreNoAplicado_NoSeReintenta_YElCarrilSigue()
    {
        var items = new[] { Reclamado(), Reclamado() };
        var (processor, _, proceso, _, _) = Crear(Settings(slots: 1), items);
        proceso.Aplicar = false;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Incluidos.Count == 2, "los dos cierres se intentan una vez");
        await Task.Delay(150, Ct);
        await processor.StopAsync(Ct);

        proceso.Incluidos.Should().HaveCount(2, "un cierre rechazado (lote cancelado o reserva vencida) no se repite");
        proceso.Omitidos.Should().BeEmpty();
        proceso.Reprogramados.Should().BeEmpty();
    }

    // ── AC6 bis (S4) ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AC6bis_MotorApagadoOSinFila_NoReclamaNada(bool? activo)
    {
        var (processor, repo, _, origen, _) = Crear(activo is null ? null : Settings(activo: activo.Value), Reclamado());

        await processor.StartAsync(Ct);
        await EsperarAsync(() => repo.LecturasSettings >= 3, "sigue sondeando los parámetros");
        await processor.StopAsync(Ct);

        repo.Reclamos.Should().Be(0);
        repo.Iniciados.Should().Be(0, "apagado no toca ni los lotes vacíos");
        origen.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task AC6bis_AlEncender_RetomaLosPendientes()
    {
        var (processor, repo, proceso, _, _) = Crear(Settings(activo: false), Reclamado(), Reclamado());

        await processor.StartAsync(Ct);
        await EsperarAsync(() => repo.LecturasSettings >= 2, "lee los parámetros apagado");
        repo.Settings = Settings(activo: true);
        await EsperarAsync(() => proceso.Incluidos.Count == 2, "retoma al encender");
        await processor.StopAsync(Ct);

        repo.Iniciados.Should().BeGreaterThan(0, "con el motor encendido también arranca los lotes sin ítems");
    }

    // ── HU #13377: cierre del carril de ítems ───────────────────────────────────────────────

    [Fact]
    public async Task HU13377_LotesConCarrilTerminado_SeCierranEnCadaCiclo_Y_UnFalloNoFrenaAlResto()
    {
        var (processor, repo, _, _, log) = Crear(Settings());
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        repo.CarrilTerminado[a] = true;
        repo.CarrilTerminado[b] = true;
        repo.FallaAlCerrar = a;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => !repo.CarrilTerminado.ContainsKey(b), "el lote b se cierra aunque el a falle");
        await EsperarAsync(() => repo.CierresPedidos.Count(id => id == a) >= 2, "el lote a se reintenta en el ciclo siguiente");
        await processor.StopAsync(Ct);

        log.Entradas.Should().Contain(e => e.Mensaje.Contains(b.ToString(), StringComparison.Ordinal)
                                          && e.Mensaje.Contains("empaquetando", StringComparison.Ordinal));
        log.Entradas.Should().Contain(e => e.Nivel == LogLevel.Error && e.Mensaje.Contains(a.ToString(), StringComparison.Ordinal)
                                          && e.Mensaje.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        log.Entradas.Should().NotContain(e => e.Mensaje.Contains("fallo simulado", StringComparison.Ordinal),
            "solo el tipo de la excepción, nunca su mensaje");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task HU13377_MotorApagado_NoCierraNingunLote(bool? activo)
    {
        var (processor, repo, _, _, _) = Crear(activo is null ? null : Settings(activo: activo.Value));
        repo.CarrilTerminado[Guid.NewGuid()] = true;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => repo.LecturasSettings >= 3, "sigue sondeando los parámetros");
        await processor.StopAsync(Ct);

        repo.CierresPedidos.Should().BeEmpty();
    }

    // ── Reanudación ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reanudacion_ItemReReclamadoConIntentosAgotados_SeOmiteSinEjecutar()
    {
        var (processor, _, proceso, origen, _) = Crear(Settings(maxIntentos: 3), Reclamado(attempts: 3));

        await processor.StartAsync(Ct);
        await EsperarAsync(() => proceso.Omitidos.Count == 1, "se omite");
        await processor.StopAsync(Ct);

        origen.Llamadas.Should().BeEmpty("un trámite que tumbó el proceso max_item_attempts veces no se vuelve a ejecutar");
        proceso.Omitidos.Single().Codigo.Should().Be(ConsolidadoLoteOmisiones.ErrorTecnico);
        proceso.Omitidos.Single().Intentos.Should().Be(3);
    }

    [Fact]
    public async Task Parada_ElItemEnVueloNoSeCierra_QuedaParaElLease()
    {
        var (processor, _, proceso, origen, _) = Crear(Settings(), Reclamado());
        origen.Bloquear = true;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => origen.Llamadas.Count == 1, "el ítem está en vuelo");
        await processor.StopAsync(Ct);

        origen.Cancelaciones.Should().Be(1);
        proceso.Incluidos.Should().BeEmpty();
        proceso.Omitidos.Should().BeEmpty();
        proceso.Reprogramados.Should().BeEmpty("una parada del host no es un fallo del trámite: lo retoma el reclamo por lease");
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed class MarcaDeScope
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class FakeRepo : IConsolidadoLoteRepository
    {
        private int _reclamos;
        private int _lecturas;
        private int _iniciados;

        private volatile ConsolidadoExportSettings? _settings;

        public ConsolidadoExportSettings? Settings { get => _settings; set => _settings = value; }
        public ConcurrentQueue<ItemLoteReclamado> Pendientes { get; } = new();
        public ConcurrentQueue<int> LeasesPedidos { get; } = new();
        public int Reclamos => _reclamos;
        public int LecturasSettings => _lecturas;
        public int Iniciados => _iniciados;

        public Task<ConsolidadoExportSettings?> ObtenerSettingsAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _lecturas);
            return Task.FromResult(_settings);
        }

        public Task<ItemLoteReclamado?> ReclamarSiguienteItemAsync(string reclamante, int leaseSegundos, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reclamos);
            LeasesPedidos.Enqueue(leaseSegundos);
            return Task.FromResult(Pendientes.TryDequeue(out var r) ? r : null);
        }

        public Task<int> IniciarLotesSinItemsAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _iniciados);
            return Task.FromResult(0);
        }

        /// <summary>HU #13377 — lotes con el carril terminado que devuelve el sondeo (se vacía al cerrarlos).</summary>
        public ConcurrentDictionary<Guid, bool> CarrilTerminado { get; } = new();

        /// <summary>HU #13377 — lotes que el procesador pidió cerrar (en orden).</summary>
        public ConcurrentQueue<Guid> CierresPedidos { get; } = new();

        /// <summary>HU #13377 — si es <c>true</c>, <see cref="CerrarCarrilAsync"/> lanza (fallo de BD en un lote).</summary>
        public Guid? FallaAlCerrar { get; set; }

        public Task<IReadOnlyList<Guid>> ObtenerLotesConCarrilTerminadoAsync(int maximo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. CarrilTerminado.Keys.Take(maximo)]);

        public Task<CierreCarrilResultado> CerrarCarrilAsync(Guid loteId, CancellationToken ct = default)
        {
            CierresPedidos.Enqueue(loteId);
            if (loteId == FallaAlCerrar)
                throw new InvalidOperationException("fallo simulado");
            CarrilTerminado.TryRemove(loteId, out _);
            return Task.FromResult(new CierreCarrilResultado(true, 1, 1));
        }

        public Task<Guid?> ObtenerLoteActivoIdAsync(Guid usuarioId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CrearLoteResultado> CrearAsync(NuevoLoteConsolidados nuevo, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> PurgarAsync(Guid loteId, DateTimeOffset ahora, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProceso : IConsolidadoLoteItemProceso
    {
        public bool Aplicar { get; set; } = true;
        public ConcurrentQueue<LoteItemIncluido> Incluidos { get; } = new();
        public ConcurrentQueue<LoteItemOmitido> Omitidos { get; } = new();
        public ConcurrentQueue<LoteItemReintento> Reprogramados { get; } = new();

        public Task<bool> MarcarIncluidoAsync(LoteItemIncluido cierre, CancellationToken ct = default)
        {
            Incluidos.Enqueue(cierre);
            return Task.FromResult(Aplicar);
        }

        public Task<bool> MarcarOmitidoAsync(LoteItemOmitido cierre, CancellationToken ct = default)
        {
            Omitidos.Enqueue(cierre);
            return Task.FromResult(Aplicar);
        }

        public Task<bool> ReprogramarAsync(LoteItemReintento reintento, CancellationToken ct = default)
        {
            Reprogramados.Enqueue(reintento);
            return Task.FromResult(Aplicar);
        }
    }

    private sealed class FakeOrigen
    {
        private int _enCurso;
        private int _max;
        private int _cancelaciones;

        public TimeSpan Retardo { get; set; }
        public bool Bloquear { get; set; }
        public ConcurrentQueue<(Guid Scope, LoteItemContexto Contexto)> Llamadas { get; } = new();
        public int MaxConcurrencia => _max;
        public int Cancelaciones => _cancelaciones;

        public async Task<LoteItemEntregaResult> EntregarAsync(Guid scope, LoteItemContexto contexto, CancellationToken ct)
        {
            Llamadas.Enqueue((scope, contexto));
            var actual = Interlocked.Increment(ref _enCurso);
            int previo;
            do
            {
                previo = _max;
            }
            while (actual > previo && Interlocked.CompareExchange(ref _max, actual, previo) != previo);

            try
            {
                if (Bloquear)
                    await Task.Delay(Timeout.Infinite, ct);
                else if (Retardo > TimeSpan.Zero)
                    await Task.Delay(Retardo, ct);
                return LoteItemEntregaResult.Incluido(
                    new LoteItemAdjunto(Guid.NewGuid(), "fm/x", 10, "sha", "c.pdf"), LoteDeliveryMode.Existente);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelaciones);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _enCurso);
            }
        }
    }

    private sealed class OrigenEnScope(FakeOrigen origen, MarcaDeScope marca) : ILoteItemOrigen
    {
        public string Origen => ConsolidadoExportOrigin.Tramites;

        public Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default) => Task.FromResult(true);

        public Task<LoteItemEntregaResult> EntregarAsync(LoteItemContexto contexto, CancellationToken ct = default) =>
            origen.EntregarAsync(marca.Id, contexto, ct);
    }

    private sealed class ListLogger : ILogger<ConsolidadoLoteProcessor>
    {
        public ConcurrentQueue<(LogLevel Nivel, string Mensaje)> Entradas { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Enqueue((logLevel, formatter(state, exception)));
    }
}
