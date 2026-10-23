using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12795 (Épica #12760) — un trabajo de la cola de regeneración anticipada.
///
/// Uso de ejemplo:
/// <code>
/// var handler = new RegenerarConsolidadoAnticipadoHandler(repo, wizardHandler, maestroHandler, logger);
/// var resultado = await handler.HandleAsync(tenantId, procedureInstanceId, TipoConsolidado.Maestro, ct);
/// // resultado: Regenerado | OmitidoVigente | OmitidoEstadoFinal | OmitidoMigrado | OmitidoCargadoManual | ...
/// </code>
///
/// Los handlers de generación son los REALES (con merger/storage falsos): así se verifica que no hay
/// composición duplicada y que "no regenerar" significa de verdad que ningún PDF se escribió.
/// </summary>
public sealed class RegenerarConsolidadoAnticipadoHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly FakeLogger _logger = new();
    private readonly RegenerarConsolidadoAnticipadoHandler _handler;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RegenerarConsolidadoAnticipadoHandlerTests()
    {
        var merger = new FakeMerger();
        _handler = new RegenerarConsolidadoAnticipadoHandler(
            _repo,
            new GenerarConsolidadoHandler(_repo, merger, _storage),
            new GenerarConsolidadoMaestroHandler(_repo, merger, _storage),
            _logger);
    }

    // ── AC3 — idempotencia frente al perezoso ───────────────────────────────────────────────────

    [Theory]
    [InlineData(TipoConsolidado.Wizard)]
    [InlineData(TipoConsolidado.Maestro)]
    public async Task AC3_BanderaYaEnTrue_TerminaSinRegenerar(TipoConsolidado documento)
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        AgregarConsolidado(instance, documento, source: "system");
        if (documento == TipoConsolidado.Wizard) instance.ConsolidadoWizardVigente = true;
        else instance.ConsolidadoMaestroVigente = true;
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, documento, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoVigente);
        _storage.Saved.Should().BeEmpty("no se escribe ningún PDF nuevo");
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _logger.Entries.Should().ContainSingle(e => e.Message.Contains("OmitidoVigente", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC3_BanderaEnTrueSinAdjunto_NoSeConsideraVigente_YRegenera()
    {
        // Mismo criterio que el atajo de caché de los handlers: bandera arriba sin PDF no es vigente.
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        instance.ConsolidadoMaestroVigente = true;
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        _storage.Saved.Should().ContainSingle(p => p.Contains("consolidado_maestro", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC3_BanderaDelOtroDocumento_NoBloquea()
    {
        // La vigencia es por documento (D2): el wizard vigente no hace vigente al maestro.
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        AgregarConsolidado(instance, TipoConsolidado.Wizard, source: "system");
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoMaestroVigente = false;
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
    }

    // ── AC4 — excepciones ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado, TipoConsolidado.Wizard)]
    [InlineData(TramiteEstado.Anulado, TipoConsolidado.Maestro)]
    [InlineData(TramiteEstado.Revocado, TipoConsolidado.Maestro)]
    [InlineData(TramiteEstado.Aprobado, TipoConsolidado.Maestro)]
    public async Task AC4_EstadoFinal_TerminaSinRegenerar_YRegistraElMotivo(string estado, TipoConsolidado documento)
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        instance.Status = estado;
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, documento, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoEstadoFinal);
        _storage.Saved.Should().BeEmpty();
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        var entrada = _logger.Entries.Should().ContainSingle().Subject;
        entrada.Level.Should().Be(LogLevel.Information);
        entrada.Message.Should().Contain("OmitidoEstadoFinal").And.Contain(id.ToString()).And.Contain(tenant.ToString());
        entrada.State.Should().Contain(kv => kv.Key == "Motivo"
            && Equals(kv.Value, ResultadoRegeneracionAnticipada.OmitidoEstadoFinal), "el log es estructurado");
    }

    [Fact]
    public async Task AC4_ConsolidadoCargadoAMano_TerminaSinRegenerar_AunqueLaBanderaEsteAbajo()
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        AgregarConsolidado(instance, TipoConsolidado.Wizard, source: "user");
        instance.ConsolidadoWizardVigente = false; // bandera abajo: sin la excepción, regeneraría
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Wizard, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoCargadoManual);
        _storage.Saved.Should().BeEmpty();
        _logger.Entries.Should().ContainSingle(e => e.Message.Contains("OmitidoCargadoManual", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC4_MaestroCargadoAMano_TambienSeProtege()
    {
        // El handler maestro no mira Source: la protección la pone este trabajo.
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        AgregarConsolidado(instance, TipoConsolidado.Maestro, source: "user");
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoCargadoManual);
        _storage.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador, TipoConsolidado.Wizard, ResultadoRegeneracionAnticipada.OmitidoMigrado)]
    [InlineData(TramiteEstado.Borrador, TipoConsolidado.Maestro, ResultadoRegeneracionAnticipada.OmitidoMigrado)]
    [InlineData(TramiteEstado.Aprobado, TipoConsolidado.Maestro, ResultadoRegeneracionAnticipada.OmitidoEstadoFinal)]
    public async Task AC4_MigradoV1_TerminaSinRegenerar(
        string estado, TipoConsolidado documento, ResultadoRegeneracionAnticipada esperado)
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        instance.Status = estado;
        instance.IsMigrated = true;
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, documento, Ct);

        resultado.Should().Be(esperado);
        _storage.Saved.Should().BeEmpty();
        _logger.Entries.Should().ContainSingle(e => e.Message.Contains(esperado.ToString(), StringComparison.Ordinal));
    }

    // ── Camino feliz y contrato ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Maestro_ConBanderaAbajo_DelegaEnElHandlerMaestro_YRegenera()
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        _storage.Saved.Should().ContainSingle(p => p.Contains("consolidado_maestro", StringComparison.Ordinal));
        instance.ConsolidadoMaestroVigente.Should().BeTrue("el handler de siempre sube la bandera");
        _logger.Entries.Should().ContainSingle(e => e.Message.Contains("regenerado por anticipado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wizard_ConBanderaAbajo_DelegaEnElHandlerDelWizard_SinForce_YRegenera()
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant);
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Wizard, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        _storage.Saved.Should().ContainSingle(p => p.Contains($"/consolidado_", StringComparison.Ordinal));
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        await _repo.Received().GetByIdWithChecklistGraphAsync(id, tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TramiteInexistente_TerminaComoNoEncontrado_SinInvocarHandlers()
    {
        var (id, tenant) = Ids();
        _repo.GetByIdWithAttachmentsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns((ProcedureInstance?)null);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Wizard, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.NoEncontrado);
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ErrorDelHandler_TerminaComoFallido_YRegistraWarning()
    {
        var (id, tenant) = Ids();
        var instance = Instancia(id, tenant, conAdjuntos: false); // maestro sin fuentes ⇒ sin_adjuntos
        Cablear(instance);

        var resultado = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Fallido);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("sin_adjuntos", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC5_ElTenantDelTrabajoViajaATodasLasLecturas()
    {
        var (id, tenant) = Ids();
        var otroTenant = Guid.NewGuid();
        var instance = Instancia(id, tenant);
        Cablear(instance);

        var resultadoOtro = await _handler.HandleAsync(otroTenant, id, TipoConsolidado.Maestro, Ct);
        var resultadoPropio = await _handler.HandleAsync(tenant, id, TipoConsolidado.Maestro, Ct);

        resultadoOtro.Should().Be(ResultadoRegeneracionAnticipada.NoEncontrado,
            "con otro tenant el repositorio no encuentra el trámite");
        resultadoPropio.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        await _repo.Received(1).GetByIdWithChecklistGraphAsync(id, tenant, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetByIdWithChecklistGraphAsync(id, otroTenant, Arg.Any<CancellationToken>());
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private static (Guid Id, Guid Tenant) Ids() => (Guid.NewGuid(), Guid.NewGuid());

    private void Cablear(ProcedureInstance instance)
    {
        // Solo responde al tenant dueño: cualquier otro tenant ve null (filtro explícito del repo real).
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(Guid id, Guid tenantId, bool conAdjuntos = true)
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

        if (conAdjuntos)
        {
            Adjuntar(instance, "fur", "system");
            Adjuntar(instance, "factura", "user");
        }

        return instance;
    }

    private void AgregarConsolidado(ProcedureInstance instance, TipoConsolidado documento, string source) =>
        Adjuntar(instance, documento == TipoConsolidado.Wizard ? "consolidado" : "consolidado_maestro", source);

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

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Saved.Count}";
            Files[path] = ms.ToArray();
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class FakeLogger : ILogger<RegenerarConsolidadoAnticipadoHandler>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var kv = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), kv));
        }
    }
}
