using System.Diagnostics;
using Flit.Infrastructure.Messaging;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #12795 (Épica #12760) — cola de regeneración anticipada: canal acotado + worker con debounce y
/// coalescing por (tenant, trámite, documento), un scope de DI por trabajo.
///
/// Uso de ejemplo:
/// <code>
/// // En un hito (HU #12796), tras confirmar la operación:
/// queue.Encolar(clientTenantId, procedureInstanceId, TipoConsolidado.Maestro); // no espera al PDF
/// </code>
///
/// El tiempo del debounce se controla con un <see cref="TimeProvider"/> falso y los métodos
/// <c>DrenarCola</c>/<c>ProcesarVencidasAsync</c> del worker: sin <c>Task.Delay</c> real salvo en la
/// única prueba del bucle completo, que usa una ventana de milisegundos.
/// </summary>
public sealed class ConsolidadoRegeneracionProcessorTests
{
    private static readonly TimeSpan Ventana = TimeSpan.FromSeconds(5);

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 23, 15, 0, 0, TimeSpan.Zero));
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly List<(Guid Tenant, int Scope)> _lecturasPorScope = [];
    private int _scopes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── AC1 — encolado no bloqueante ────────────────────────────────────────────────────────────

    [Fact]
    public void AC1_Encolar_VuelveDeInmediato_SinEjecutarLaGeneracion()
    {
        var (queue, processor) = Crear();
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());

        var reloj = Stopwatch.StartNew();
        var aceptada = queue.Encolar(tenant, id, TipoConsolidado.Wizard);
        reloj.Stop();

        aceptada.Should().BeTrue();
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200), "no espera a la generación del PDF");
        _repo.ReceivedCalls().Should().BeEmpty("nada se ejecuta en el hilo del llamador");
        _scopes.Should().Be(0);
        processor.DrenarCola().Should().Be(1, "la solicitud quedó en el canal para el worker");
    }

    [Fact]
    public void AC1_ColaLlena_DevuelveFalse_SinBloquear()
    {
        var (queue, _) = Crear(capacidad: 1);

        queue.Encolar(Guid.NewGuid(), Guid.NewGuid(), TipoConsolidado.Wizard).Should().BeTrue();
        var reloj = Stopwatch.StartNew();
        var segunda = queue.Encolar(Guid.NewGuid(), Guid.NewGuid(), TipoConsolidado.Maestro);
        reloj.Stop();

        segunda.Should().BeFalse("con el canal lleno la solicitud se descarta y el llamador lo sabe");
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "5b1d4c1e-0000-4000-8000-000000000001", 0)]
    [InlineData("5b1d4c1e-0000-4000-8000-000000000002", "00000000-0000-0000-0000-000000000000", 1)]
    [InlineData("5b1d4c1e-0000-4000-8000-000000000003", "5b1d4c1e-0000-4000-8000-000000000004", 7)]
    public void AC1_Contrato_SolicitudInvalida_SeRechaza(string tenant, string instancia, int documento)
    {
        var (queue, processor) = Crear();

        queue.Encolar(Guid.Parse(tenant), Guid.Parse(instancia), (TipoConsolidado)documento).Should().BeFalse();
        processor.DrenarCola().Should().Be(0);
    }

    // ── AC2 — coalescing dentro de la ventana ───────────────────────────────────────────────────

    [Fact]
    public async Task AC2_TresSolicitudesDelMismoTramiteYDocumento_DentroDeLaVentana_UnaSolaRegeneracion()
    {
        var (queue, processor) = Crear();
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());
        Cablear(Instancia(id, tenant));

        for (var i = 0; i < 3; i++)
        {
            queue.Encolar(tenant, id, TipoConsolidado.Maestro).Should().BeTrue();
            processor.DrenarCola();
            (await processor.ProcesarVencidasAsync(Ct)).Should().Be(0, "la ventana no ha vencido");
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        processor.Pendientes.Should().Be(1, "las tres solicitudes se fundieron en una clave");

        // Última solicitud en t+2s ⇒ vence en t+7s (debounce de cola). En t+6s aún no.
        _clock.Advance(TimeSpan.FromSeconds(3)); // t+6s
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(0);

        _clock.Advance(TimeSpan.FromSeconds(1)); // t+7s
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(1);

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(0, "no queda nada pendiente");

        await _repo.Received(1).GetByIdWithAttachmentsAsync(id, tenant, Arg.Any<CancellationToken>());
        _scopes.Should().Be(1);
    }

    [Fact]
    public async Task AC2_ClavesDistintas_NoSeFunden()
    {
        var (queue, processor) = Crear();
        var tenant = Guid.NewGuid();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        Cablear(Instancia(a, tenant));
        Cablear(Instancia(b, tenant));

        queue.Encolar(tenant, a, TipoConsolidado.Wizard);
        queue.Encolar(tenant, a, TipoConsolidado.Maestro); // mismo trámite, otro documento
        queue.Encolar(tenant, b, TipoConsolidado.Wizard);  // otro trámite
        processor.DrenarCola();

        _clock.Advance(Ventana);
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(3);
        _scopes.Should().Be(3);
    }

    [Fact]
    public async Task AC2_SolicitudesSinPausa_SeEjecutanAlCumplirLaEsperaMaxima()
    {
        var (queue, processor) = Crear(esperaMaxima: TimeSpan.FromSeconds(12));
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());
        Cablear(Instancia(id, tenant));

        // Una solicitud cada 4s: la ventana (5s) se reiniciaría para siempre sin el tope.
        for (var t = 0; t <= 12; t += 4)
        {
            queue.Encolar(tenant, id, TipoConsolidado.Wizard);
            processor.DrenarCola();
            if (t < 12)
            {
                (await processor.ProcesarVencidasAsync(Ct)).Should().Be(0);
                _clock.Advance(TimeSpan.FromSeconds(4));
            }
        }

        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(1, "a los 12s manda el tope");
    }

    // ── AC3 — idempotencia frente al perezoso, a través del worker ──────────────────────────────

    [Fact]
    public async Task AC3_SiElPerezosoReconstruyoMientrasEsperaba_ElTrabajoTerminaSinRegenerar()
    {
        var (queue, processor) = Crear();
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instancia(id, tenant);
        Cablear(instance);

        queue.Encolar(tenant, id, TipoConsolidado.Maestro);
        processor.DrenarCola();

        // Mientras la solicitud espera, alguien abre el consolidado: el camino perezoso lo reconstruye.
        instance.ConsolidadoMaestroVigente = true;
        Adjuntar(instance, "consolidado_maestro", "system");

        _clock.Advance(Ventana);
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(1);

        _storage.Saved.Should().BeEmpty("el trabajo no regeneró");
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── AC4 — excepciones, a través del worker ──────────────────────────────────────────────────

    [Theory]
    [InlineData("final")]
    [InlineData("user")]
    [InlineData("migrado")]
    public async Task AC4_Excepciones_ElTrabajoTerminaSinRegenerar(string caso)
    {
        var (queue, processor) = Crear();
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());
        var instance = Instancia(id, tenant);
        switch (caso)
        {
            case "final": instance.Status = TramiteEstado.Aprobado; break;
            case "user": Adjuntar(instance, "consolidado", "user"); break;
            case "migrado": instance.IsMigrated = true; break;
        }

        Cablear(instance);

        queue.Encolar(tenant, id, TipoConsolidado.Wizard);
        processor.DrenarCola();
        _clock.Advance(Ventana);
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(1);

        _storage.Saved.Should().BeEmpty();
    }

    // ── AC5 — aislamiento por tenant ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC5_CadaTrabajoCorreEnSuPropioScope_ConElTenantPropietario()
    {
        var (queue, processor) = Crear();
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var (idA, idB) = (Guid.NewGuid(), Guid.NewGuid());
        Cablear(Instancia(idA, tenantA));
        Cablear(Instancia(idB, tenantB));

        queue.Encolar(tenantA, idA, TipoConsolidado.Maestro);
        queue.Encolar(tenantB, idB, TipoConsolidado.Maestro);
        processor.DrenarCola();
        _clock.Advance(Ventana);
        (await processor.ProcesarVencidasAsync(Ct)).Should().Be(2);

        _scopes.Should().Be(2, "un scope nuevo por trabajo");
        _lecturasPorScope.Should().HaveCount(2);
        _lecturasPorScope.Select(l => l.Scope).Should().OnlyHaveUniqueItems("ningún trabajo reutiliza el scope de otro");
        _lecturasPorScope.Select(l => l.Tenant).Should().BeEquivalentTo([tenantA, tenantB]);
        await _repo.Received(1).GetByIdWithAttachmentsAsync(idA, tenantA, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetByIdWithAttachmentsAsync(idB, tenantB, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithAttachmentsAsync(idA, tenantB, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithAttachmentsAsync(idB, tenantA, Arg.Any<CancellationToken>());
        _storage.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task AC5_UnTrabajoQueLanza_NoImpideLosDemas()
    {
        var (queue, processor) = Crear();
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var (idA, idB) = (Guid.NewGuid(), Guid.NewGuid());
        _repo.GetByIdWithAttachmentsAsync(idA, tenantA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        Cablear(Instancia(idB, tenantB));

        queue.Encolar(tenantA, idA, TipoConsolidado.Maestro);
        queue.Encolar(tenantB, idB, TipoConsolidado.Maestro);
        processor.DrenarCola();
        _clock.Advance(Ventana);

        var act = () => processor.ProcesarVencidasAsync(Ct);

        (await act.Should().NotThrowAsync()).Subject.Should().Be(2);
        _storage.Saved.Should().ContainSingle();
    }

    // ── Bucle completo (tiempo real, ventana de milisegundos) ───────────────────────────────────

    [Fact]
    public async Task Bucle_EjecutaTrasLaVentana_YFundeLasSolicitudesRapidas()
    {
        var (queue, processor) = Crear(
            ventana: TimeSpan.FromMilliseconds(150), reloj: TimeProvider.System);
        var (tenant, id) = (Guid.NewGuid(), Guid.NewGuid());
        Cablear(Instancia(id, tenant));

        await processor.StartAsync(Ct);
        try
        {
            for (var i = 0; i < 3; i++)
                queue.Encolar(tenant, id, TipoConsolidado.Maestro).Should().BeTrue();

            var limite = Stopwatch.StartNew();
            while (_storage.Saved.Count == 0 && limite.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(20, Ct);

            _storage.Saved.Should().ContainSingle("el worker regeneró tras la ventana");
            await Task.Delay(400, Ct); // margen para una hipotética segunda ejecución
            _storage.Saved.Should().ContainSingle("tres solicitudes rápidas = una regeneración");
            _scopes.Should().Be(1);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    // ── Composición real (Program) ──────────────────────────────────────────────────────────────

    [Fact]
    public void Composicion_LaColaEsSingleton_YElWorkerEstaRegistrado()
    {
        using var factory = new WebApplicationFactory<Program>();
        var services = factory.Services;

        var cola = services.GetRequiredService<IConsolidadoRegeneracionQueue>();
        cola.Should().BeOfType<ChannelConsolidadoRegeneracionQueue>();
        cola.Should().BeSameAs(services.GetRequiredService<IConsolidadoRegeneracionQueue>());
        cola.Should().BeSameAs(services.GetRequiredService<ChannelConsolidadoRegeneracionQueue>(),
            "el worker lee del mismo canal en el que escriben los hitos");
        services.GetServices<IHostedService>().OfType<ConsolidadoRegeneracionProcessor>().Should().ContainSingle();

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RegenerarConsolidadoAnticipadoHandler>().Should().NotBeNull();
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private readonly FakeStorage _storage = new();

    private (ChannelConsolidadoRegeneracionQueue Queue, ConsolidadoRegeneracionProcessor Processor) Crear(
        int capacidad = 1_024,
        TimeSpan? ventana = null,
        TimeSpan? esperaMaxima = null,
        TimeProvider? reloj = null)
    {
        var options = Options.Create(new ConsolidadoRegeneracionOptions
        {
            Capacidad = capacidad,
            VentanaDebounce = ventana ?? Ventana,
            EsperaMaxima = esperaMaxima ?? TimeSpan.FromSeconds(30),
        });

        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var scope = Interlocked.Increment(ref _scopes);
            // Envoltura por scope: registra con qué tenant lee cada scope y delega en el sustituto.
            return new RepoPorScope(_repo, scope, _lecturasPorScope);
        });
        services.AddScoped<IProcedureInstanceRepository>(sp => sp.GetRequiredService<RepoPorScope>().Proxy);
        services.AddSingleton<IExpedienteConsolidadoMerger, FakeMerger>();
        services.AddSingleton<IAttachmentStorage>(_storage);
        services.AddScoped(sp => new GenerarConsolidadoHandler(
            sp.GetRequiredService<IProcedureInstanceRepository>(),
            sp.GetRequiredService<IExpedienteConsolidadoMerger>(),
            sp.GetRequiredService<IAttachmentStorage>()));
        services.AddScoped(sp => new GenerarConsolidadoMaestroHandler(
            sp.GetRequiredService<IProcedureInstanceRepository>(),
            sp.GetRequiredService<IExpedienteConsolidadoMerger>(),
            sp.GetRequiredService<IAttachmentStorage>()));
        services.AddScoped(sp => new RegenerarConsolidadoAnticipadoHandler(
            sp.GetRequiredService<IProcedureInstanceRepository>(),
            sp.GetRequiredService<GenerarConsolidadoHandler>(),
            sp.GetRequiredService<GenerarConsolidadoMaestroHandler>()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var queue = new ChannelConsolidadoRegeneracionQueue(options);
        var processor = new ConsolidadoRegeneracionProcessor(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            reloj ?? _clock,
            NullLogger<ConsolidadoRegeneracionProcessor>.Instance);
        return (queue, processor);
    }

    private void Cablear(ProcedureInstance instance)
    {
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(Guid id, Guid tenantId)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012795",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Adjuntar(instance, "fur", "system");
        Adjuntar(instance, "factura", "user");
        return instance;
    }

    private void Adjuntar(ProcedureInstance instance, string tipo, string source)
    {
        var path = $"{instance.Id:D}/{tipo}-previo";
        _storage.Files[path] = System.Text.Encoding.UTF8.GetBytes($"%PDF-{tipo}");
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = 10,
            Sha256 = $"sha-{tipo}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Reloj controlable: solo <c>GetUtcNow</c>, que es lo que usa el debounce.</summary>
    private sealed class FakeClock(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Advance(TimeSpan delta) => _ahora += delta;
    }

    /// <summary>
    /// Repositorio por scope: cada scope obtiene un proxy propio que anota (tenant, scope) en cada
    /// lectura del trabajo y delega en el sustituto compartido.
    /// </summary>
    private sealed class RepoPorScope
    {
        public RepoPorScope(IProcedureInstanceRepository inner, int scope, List<(Guid Tenant, int Scope)> lecturas)
        {
            Proxy = Substitute.For<IProcedureInstanceRepository>();
            Proxy.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    lock (lecturas)
                        lecturas.Add((ci.ArgAt<Guid>(1), scope));
                    return inner.GetByIdWithAttachmentsAsync(ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<CancellationToken>(2));
                });
            Proxy.GetByIdWithChecklistGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => inner.GetByIdWithChecklistGraphAsync(
                    ci.ArgAt<Guid>(0), ci.ArgAt<Guid>(1), ci.ArgAt<CancellationToken>(2)));
        }

        public IProcedureInstanceRepository Proxy { get; }
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        private readonly object _gate = new();

        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            lock (_gate)
            {
                var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
                Files[path] = ms.ToArray();
                Saved.Add(path);
                return new StoredFile(path, $"sha-{tipo}", ms.Length);
            }
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
            lock (_gate)
                Files.Remove(storagePath);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        {
            lock (_gate)
                return Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);
        }

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
