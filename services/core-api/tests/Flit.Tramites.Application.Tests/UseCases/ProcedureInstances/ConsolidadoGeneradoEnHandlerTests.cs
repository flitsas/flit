using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12790 (Épica #12760) — sello de tiempo de generación de cada consolidado.
/// <para>
/// Los tres handlers toman el instante con <c>DateTimeOffset.UtcNow</c> en una única variable
/// <c>now</c> que ya sellaba <c>UploadedAt</c> del adjunto y <c>CreatedAt</c> del evento; el sello
/// nuevo reutiliza esa misma marca. Por eso la prueba acota el instante entre dos lecturas del reloj
/// (antes/después) y exige igualdad EXACTA con el <c>UploadedAt</c> del consolidado persistido y
/// desfase cero (UTC).
/// </para>
/// <para>
/// Uso de ejemplo:
/// <c>await new GenerarConsolidadoHandler(repo, merger, storage).HandleAsync(id, tenantId, ct);</c>
/// → <c>instance.ConsolidadoWizardGeneradoEn == consolidado.UploadedAt</c>.
/// </para>
/// </summary>
public sealed class ConsolidadoGeneradoEnHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeMerger _merger = new();
    private readonly FakeStorage _storage = new();

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        private int _saved;

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var path = $"{procedureInstanceId:D}/{tipo}_{_saved++}";
            Files[path] = bytes;
            return new StoredFile(path, $"sha-{tipo}-{_saved}", bytes.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Files.TryGetValue(storagePath, out var bytes)
                ? Task.FromResult<Stream?>(new MemoryStream(bytes))
                : Task.FromResult<Stream?>(null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private ProcedureInstance Instancia(Guid id, Guid tenantId, params (string Tipo, string Source)[] adjuntos)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012790",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (tipo, source) in adjuntos)
        {
            var path = $"{id:D}/{tipo}_{source}_{Guid.NewGuid():N}";
            _storage.Files[path] = Encoding.UTF8.GetBytes($"%PDF-{tipo}");
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = id,
                Tipo = tipo,
                Filename = $"{tipo}.pdf",
                Mimetype = "application/pdf",
                SizeBytes = 10,
                Sha256 = $"sha-{tipo}-{source}",
                StoragePath = path,
                Source = source,
                UploadedAt = DateTimeOffset.UtcNow.AddDays(-3),
            });
        }
        return instance;
    }

    private static CargarConsolidadoExternoInput PdfInput() =>
        new("externo.pdf", "application/pdf", 100, new MemoryStream(Encoding.UTF8.GetBytes("%PDF-externo")));

    // ── AC2 — generación del wizard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_GenerarWizard_SellaConsolidadoWizardGeneradoEnConElInstanteUtc()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var antes = DateTimeOffset.UtcNow;

        var (result, error) = await new GenerarConsolidadoHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, CancellationToken.None);
        var despues = DateTimeOffset.UtcNow;

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        var sello = instance.ConsolidadoWizardGeneradoEn;
        sello.Should().NotBeNull();
        sello!.Value.Offset.Should().Be(TimeSpan.Zero, "se guarda en UTC");
        sello.Value.Should().BeOnOrAfter(antes).And.BeOnOrBefore(despues);
        var consolidado = instance.Attachments.Single(a => a.Tipo == "consolidado");
        sello.Should().Be(consolidado.UploadedAt, "el sello es la misma marca del documento persistido");
        instance.ConsolidadoMaestroGeneradoEn.Should().BeNull("el wizard no sella el maestro");
    }

    [Fact]
    public async Task AC2_ConsolidadoWizardVigenteServidoDeCache_NoResellaLaFecha()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"), ("consolidado", "system"));
        var selloPrevio = new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = selloPrevio;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await new GenerarConsolidadoHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse("vigente y sin force se sirve el persistido");
        instance.ConsolidadoWizardGeneradoEn.Should().Be(selloPrevio, "el sello es de la generación, no de la lectura");
    }

    [Fact]
    public async Task AC2_RegenerarConForce_ReemplazaElSelloAnterior()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"), ("consolidado", "system"));
        var selloPrevio = new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = selloPrevio;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await new GenerarConsolidadoHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, userId: null, force: true, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        instance.ConsolidadoWizardGeneradoEn.Should().NotBeNull().And.BeAfter(selloPrevio);
    }

    // ── AC3 — generación del maestro ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_GenerarMaestro_SellaConsolidadoMaestroGeneradoEnConElInstanteUtc()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "user"), ("factura", "user"));
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var antes = DateTimeOffset.UtcNow;

        var (result, error) = await new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, ct: CancellationToken.None);
        var despues = DateTimeOffset.UtcNow;

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        instance.ConsolidadoMaestroVigente.Should().BeTrue();
        var sello = instance.ConsolidadoMaestroGeneradoEn;
        sello.Should().NotBeNull();
        sello!.Value.Offset.Should().Be(TimeSpan.Zero, "se guarda en UTC");
        sello.Value.Should().BeOnOrAfter(antes).And.BeOnOrBefore(despues);
        sello.Should().Be(instance.Attachments.Single(a => a.Tipo == "consolidado_maestro").UploadedAt);
        instance.ConsolidadoWizardGeneradoEn.Should().BeNull("el maestro no sella el wizard");
    }

    [Fact]
    public async Task AC3_MaestroVigenteReutilizado_NoResellaLaFecha()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "user"), ("consolidado_maestro", "system"));
        var selloPrevio = new DateTimeOffset(2026, 9, 2, 10, 30, 0, TimeSpan.Zero);
        instance.ConsolidadoMaestroVigente = true;
        instance.ConsolidadoMaestroGeneradoEn = selloPrevio;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, ct: CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse();
        instance.ConsolidadoMaestroGeneradoEn.Should().Be(selloPrevio);
    }

    // ── AC4 — carga manual del SuperAdmin ───────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_CargaManual_SellaElInstanteDeLaCargaYElOrigenQuedaUser()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"));
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var antes = DateTimeOffset.UtcNow;

        var (result, error) = await new CargarConsolidadoExternoHandler(_repo, _storage)
            .HandleAsync(id, tenantId, PdfInput(), Guid.NewGuid(), CancellationToken.None);
        var despues = DateTimeOffset.UtcNow;

        error.Should().BeNull();
        result.Should().NotBeNull();
        var consolidado = instance.Attachments.Single(a => a.Tipo == "consolidado");
        consolidado.Source.Should().Be("user");
        var sello = instance.ConsolidadoWizardGeneradoEn;
        sello.Should().NotBeNull();
        sello!.Value.Offset.Should().Be(TimeSpan.Zero);
        sello.Value.Should().BeOnOrAfter(antes).And.BeOnOrBefore(despues);
        sello.Should().Be(consolidado.UploadedAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_CargaManual_ReemplazaElSelloDeUnaGeneracionPrevia()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"), ("consolidado", "system"));
        var selloPrevio = new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
        instance.ConsolidadoWizardVigente = true;
        instance.ConsolidadoWizardGeneradoEn = selloPrevio;
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await new CargarConsolidadoExternoHandler(_repo, _storage)
            .HandleAsync(id, tenantId, PdfInput(), Guid.NewGuid(), CancellationToken.None);

        error.Should().BeNull();
        instance.ConsolidadoWizardGeneradoEn.Should().NotBeNull().And.BeAfter(selloPrevio);
    }

    [Fact]
    public async Task AC4_CargaRechazadaPorSoloLectura_NoSella()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"));
        instance.IsMigrated = true;
        instance.Status = TramiteEstado.Anulado;
        _repo.GetByIdWithAttachmentsAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await new CargarConsolidadoExternoHandler(_repo, _storage)
            .HandleAsync(id, tenantId, PdfInput(), userId: null, CancellationToken.None);

        error.Should().Be("migrado_solo_lectura");
        instance.ConsolidadoWizardGeneradoEn.Should().BeNull();
    }

    // ── AC5 — trámites históricos ───────────────────────────────────────────────────────────────

    [Fact]
    public void AC5_TramiteSinGeneracion_TieneAmbosSellosEnNull()
    {
        var instance = new ProcedureInstance();

        instance.ConsolidadoWizardGeneradoEn.Should().BeNull();
        instance.ConsolidadoMaestroGeneradoEn.Should().BeNull();
    }

    [Fact]
    public async Task AC5_HistoricoConConsolidadoVigenteSinSello_SeSirveSinInvalidarYSigueNull()
    {
        // Trámite anterior a la columna: tiene consolidado vigente pero el sello es null. Leerlo NO
        // debe invalidarlo ni regenerarlo (la UI mostrará «fecha no disponible»).
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instancia(id, tenantId, ("fur", "system"), ("consolidado", "system"));
        instance.ConsolidadoWizardVigente = true;
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(instance);

        var (result, error) = await new GenerarConsolidadoHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, CancellationToken.None);

        error.Should().BeNull();
        result!.Regenerado.Should().BeFalse("el sello nulo no invalida el documento");
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        instance.ConsolidadoWizardGeneradoEn.Should().BeNull();
    }

    [Fact]
    public async Task AC5_MaestroInexistente_NoSellaNada()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithChecklistGraphAsync(id, tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);

        var (result, error) = await new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage)
            .HandleAsync(id, tenantId, ct: CancellationToken.None);

        result.Should().BeNull();
        error.Should().NotBeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
