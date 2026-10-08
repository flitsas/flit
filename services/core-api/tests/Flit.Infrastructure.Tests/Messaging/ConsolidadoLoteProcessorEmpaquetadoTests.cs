using System.Collections.Concurrent;
using Flit.Infrastructure.Messaging;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D2) — carril de empaquetado de <see cref="ConsolidadoLoteProcessor"/> sin base de
/// datos: 1 slot, <c>part_timeout_seconds</c> que cuenta intento, intentos agotados al reclamar, finalización de lotes
/// listos y parámetros incoherentes que apagan solo este carril. El <see cref="EmpaquetarParteHandler"/> es el real con
/// dobles de sus puertos. También la limpieza de temporales al arrancar (AC5).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var (processor, emp, _) = Crear(Settings(), Parte());
/// await processor.StartAsync(ct); // reclama de emp.Pendientes, empaqueta y cierra
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteProcessorEmpaquetadoTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flit-13378-proc", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static ConsolidadoExportSettings Settings(int timeout = 300, int lease = 600, short maxIntentos = 3) => new()
    {
        IsActive = true,
        ItemSlots = 2,
        ItemTimeoutSeconds = 300,
        ItemLeaseSeconds = 600,
        MaxItemAttempts = 3,
        RetryDelaySeconds = 30,
        PartTimeoutSeconds = timeout,
        PartLeaseSeconds = lease,
        MaxPartAttempts = maxIntentos,
        RetentionHours = 24,
    };

    private static ParteLoteReclamada Parte(short numero = 1, short intentos = 0)
    {
        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            Status = ConsolidadoExportStatus.Empaquetando,
            DocumentType = ConsolidadoExportDocumentType.Consolidado,
            DekWrapped = [1],
        };
        return new ParteLoteReclamada(lote, new ConsolidadoExportBatchPart
        {
            BatchId = lote.Id,
            PartNumber = numero,
            Status = ConsolidadoExportPartStatus.Empaquetando,
            Attempts = intentos,
        });
    }

    private static async Task EsperarAsync(Func<bool> condicion, string porque, int segundos = 10)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite && !condicion())
            await Task.Delay(20, Ct);
        condicion().Should().BeTrue(porque);
    }

    private (ConsolidadoLoteProcessor Processor, FakeEmpaquetado Empaquetado, FakeRepo Repo) Crear(
        ConsolidadoExportSettings settings, params ParteLoteReclamada[] partes)
    {
        var repo = new FakeRepo { Settings = settings };
        var emp = new FakeEmpaquetado();
        foreach (var p in partes)
            emp.Pendientes.Enqueue(p);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConsolidadoLoteRepository>(repo);
        services.AddSingleton<IConsolidadoLoteEmpaquetado>(emp);
        services.AddSingleton<IConsolidadoLoteCipher, CipherIdentidad>();
        services.AddSingleton<IConsolidadoLoteParteStorage, ParteStorageNula>();
        services.AddSingleton<IAttachmentStorage, AdjuntosVacios>();
        services.AddSingleton<IConsolidadoLoteAdjuntoActual, SinAdjuntoActual>();
        services.AddSingleton(new ConsolidadoLoteTemporales(_dir));
        services.AddScoped<EmpaquetarParteHandler>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var processor = new ConsolidadoLoteProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConsolidadoLoteProcessor>.Instance,
            new ConsolidadoLoteProcessorOptions(TimeSpan.FromMilliseconds(30), TimeSpan.Zero),
            TimeProvider.System);
        return (processor, emp, repo);
    }

    [Theory]
    [InlineData(300, 600, 3, true)]
    [InlineData(300, 300, 3, false)]
    [InlineData(0, 600, 3, false)]
    [InlineData(300, 600, 0, false)]
    public void ParametrosEmpaquetado_ExigenLeaseMayorQueTimeoutEIntentos(int timeout, int lease, short max, bool esperado) =>
        ConsolidadoLoteProcessor.ParametrosEmpaquetadoValidos(Settings(timeout, lease, max)).Should().Be(esperado);

    [Fact]
    public async Task AC1_UnSlot_LasPartesSeEmpaquetanDeUnaEnUna_YSeCierran()
    {
        var (processor, emp, _) = Crear(Settings(), Parte(1), Parte(2), Parte(3));
        emp.Retardo = TimeSpan.FromMilliseconds(80);

        await processor.StartAsync(Ct);
        await EsperarAsync(() => emp.Cierres.Count == 3, "las tres partes se cierran");
        await processor.StopAsync(Ct);

        emp.MaxConcurrencia.Should().Be(1, "el carril de empaquetado es de 1 slot");
        emp.Leases.Should().OnlyContain(l => l == 600, "el lease del reclamo es part_lease_seconds");
    }

    [Fact]
    public async Task AC3_Timeout_CancelaLaEjecucion_YCuentaUnIntento()
    {
        var (processor, emp, _) = Crear(Settings(timeout: 1, lease: 5), Parte(intentos: 1));
        emp.Bloquear = true;

        await processor.StartAsync(Ct);
        await EsperarAsync(() => !emp.Fallos.IsEmpty, "el timeout registra el fallo");
        await processor.StopAsync(Ct);

        emp.Fallos.Should().ContainSingle().Which.Should().Match<FalloParteLote>(f =>
            f.IntentosReclamados == 1 && f.Intentos == 2 && f.MaxIntentos == 3);
        emp.Cancelaciones.Should().Be(1);
        Directory.Exists(_dir).Should().BeFalse("no llegó a crear temporales");
    }

    [Fact]
    public async Task AC3_IntentosAgotadosAlReclamar_NoSeEjecuta_YSeRegistraElFallo()
    {
        var (processor, emp, _) = Crear(Settings(maxIntentos: 3), Parte(intentos: 3));

        await processor.StartAsync(Ct);
        await EsperarAsync(() => !emp.Fallos.IsEmpty, "se registra el fallo");
        await processor.StopAsync(Ct);

        emp.Lecturas.Should().Be(0, "una parte que ya agotó max_part_attempts no vuelve a ejecutarse");
        emp.Fallos.Single().Should().Match<FalloParteLote>(f => f.Intentos == 3 && f.MaxIntentos == 3);
    }

    [Fact]
    public async Task AC2_CadaCicloFinalizaLosLotesListos()
    {
        var (processor, emp, _) = Crear(Settings());
        var listo = Guid.NewGuid();
        emp.ParaFinalizar.TryAdd(listo, true);

        await processor.StartAsync(Ct);
        await EsperarAsync(() => emp.Finalizados.Contains(listo), "el lote listo se finaliza");
        await processor.StopAsync(Ct);
    }

    [Fact]
    public async Task ParametrosIncoherentes_ApaganSoloElCarrilDeEmpaquetado()
    {
        var (processor, emp, repo) = Crear(Settings(timeout: 600, lease: 600), Parte());

        await processor.StartAsync(Ct);
        await EsperarAsync(() => repo.Reclamos >= 2, "el carril de ítems sigue reclamando");
        await processor.StopAsync(Ct);

        emp.Leases.Should().BeEmpty("sin lease > timeout no se reclama ninguna parte");
        emp.Pendientes.Should().HaveCount(1);
    }

    [Fact]
    public async Task AC5_AlArrancar_SeBorranLosTemporalesHuerfanos()
    {
        Directory.CreateDirectory(_dir);
        var huerfano = Path.Combine(_dir, "x" + ConsolidadoLoteTemporales.ExtensionCifrado);
        await File.WriteAllTextAsync(huerfano, "x", Ct);
        var limpieza = new ConsolidadoLoteTemporalesLimpieza(
            new ConsolidadoLoteTemporales(_dir), NullLogger<ConsolidadoLoteTemporalesLimpieza>.Instance);

        await limpieza.StartAsync(Ct);
        await limpieza.StopAsync(Ct);

        File.Exists(huerfano).Should().BeFalse();
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed class FakeRepo : IConsolidadoLoteRepository
    {
        private int _reclamos;

        public ConsolidadoExportSettings? Settings { get; set; }
        public int Reclamos => _reclamos;

        public Task<ConsolidadoExportSettings?> ObtenerSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);

        public Task<ItemLoteReclamado?> ReclamarSiguienteItemAsync(string reclamante, int leaseSegundos, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reclamos);
            return Task.FromResult<ItemLoteReclamado?>(null);
        }

        public Task<int> IniciarLotesSinItemsAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<Guid>> ObtenerLotesConCarrilTerminadoAsync(int maximo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<CierreCarrilResultado> CerrarCarrilAsync(Guid loteId, CancellationToken ct = default) =>
            Task.FromResult(CierreCarrilResultado.NoAplicado);

        public Task<CancelarLoteResultado> CancelarAsync(CancelacionLote solicitud, CancellationToken ct = default) =>
            throw new NotSupportedException();

        /// <summary>HU #13386 — los checkpoints del empaquetado ven el lote vivo.</summary>
        public Task<string?> GetStatusAsync(Guid batchId, CancellationToken ct = default) =>
            Task.FromResult<string?>(ConsolidadoExportStatus.Empaquetando);

        public Task<Guid?> ObtenerLoteActivoIdAsync(Guid usuarioId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CrearLoteResultado> CrearAsync(NuevoLoteConsolidados nuevo, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> PurgarAsync(Guid loteId, DateTimeOffset ahora, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeEmpaquetado : IConsolidadoLoteEmpaquetado
    {
        private int _enCurso;
        private int _max;
        private int _lecturas;
        private int _cancelaciones;

        public ConcurrentQueue<ParteLoteReclamada> Pendientes { get; } = new();
        public ConcurrentQueue<int> Leases { get; } = new();
        public ConcurrentQueue<CierreParteLote> Cierres { get; } = new();
        public ConcurrentQueue<FalloParteLote> Fallos { get; } = new();
        public ConcurrentDictionary<Guid, bool> ParaFinalizar { get; } = new();
        public ConcurrentBag<Guid> Finalizados { get; } = [];
        public TimeSpan Retardo { get; set; }
        public bool Bloquear { get; set; }
        public int MaxConcurrencia => _max;
        public int Lecturas => _lecturas;
        public int Cancelaciones => _cancelaciones;

        public Task<ParteLoteReclamada?> ReclamarSiguienteParteAsync(int leaseSegundos, CancellationToken ct = default)
        {
            Leases.Enqueue(leaseSegundos);
            return Task.FromResult(Pendientes.TryDequeue(out var p) ? p : null);
        }

        public async Task<ContenidoParteLote> LeerContenidoAsync(Guid loteId, short partNumber, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _lecturas);
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
                return new ContenidoParteLote([], []);
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

        public Task<bool> DescartarSiLoteInactivoAsync(Guid loteId, short partNumber, short intentos, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<CierreParteDesenlace> CerrarParteAsync(CierreParteLote cierre, CancellationToken ct = default)
        {
            Cierres.Enqueue(cierre);
            return Task.FromResult(CierreParteDesenlace.Cerrada);
        }

        public Task<FalloParteDesenlace> RegistrarFalloParteAsync(FalloParteLote fallo, CancellationToken ct = default)
        {
            Fallos.Enqueue(fallo);
            return Task.FromResult(FalloParteDesenlace.Reprogramada);
        }

        public Task<bool> FallarLoteAsync(Guid loteId, string codigoError, CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<Guid>> ObtenerLotesParaFinalizarAsync(int maximo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. ParaFinalizar.Keys.Take(maximo)]);

        public Task<LoteFinalizado?> FinalizarLoteAsync(Guid loteId, CancellationToken ct = default)
        {
            Finalizados.Add(loteId);
            ParaFinalizar.TryRemove(loteId, out _);
            return Task.FromResult<LoteFinalizado?>(null);
        }
    }

    private sealed class CipherIdentidad : IConsolidadoLoteCipher
    {
        public byte[] GenerarDekEnvuelta() => [1];

        public async Task<ConsolidadoLoteCifradoResultado> CifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenEnClaro, Stream destinoCifrado, CancellationToken ct = default)
        {
            await origenEnClaro.CopyToAsync(destinoCifrado, ct);
            return new ConsolidadoLoteCifradoResultado(destinoCifrado.Length, destinoCifrado.Length, 1);
        }

        public Task<long> DescifrarAsync(
            byte[]? dekEnvuelta, Guid loteId, int partNumber, Stream origenCifrado, Stream destinoEnClaro, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ParteStorageNula : IConsolidadoLoteParteStorage
    {
        public Task<StoredFile> SubirAsync(Guid loteId, int partNumber, string rutaArchivoCifrado, CancellationToken ct = default) =>
            Task.FromResult(new StoredFile($"fm/{partNumber}", new string('a', 64), new FileInfo(rutaArchivoCifrado).Length));

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }
    }

    private sealed class AdjuntosVacios : IAttachmentStorage
    {
        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private sealed class SinAdjuntoActual : IConsolidadoLoteAdjuntoActual
    {
        public Task<string?> StoragePathActualAsync(Guid procedureInstanceId, Guid tenantId, string tipoDocumento, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
