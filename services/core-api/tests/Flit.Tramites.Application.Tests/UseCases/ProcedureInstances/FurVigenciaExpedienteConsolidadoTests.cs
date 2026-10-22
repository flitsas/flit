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
/// Bug #13055 — la placa que asigna (o corrige) el OT no aparecía en el FUR del consolidado: los
/// consolidados solo fusionan el FUR persistido y, si este era anterior a la placa, se armaban y
/// quedaban vigentes con el FUR viejo. Ahora un FUR anterior a <c>PlateAssignedAt</c>/<c>PlateUpdatedAt</c>
/// invalida la caché y se regenera antes de fusionar, en el wizard, en el maestro y en el worker.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// instance.PlateAssignedAt = furUploadedAt.AddMinutes(1); // el FUR no tiene la placa
/// await maestroHandler.HandleAsync(id, tenant, ct: ct);  // regenera el FUR y luego fusiona
/// </code>
/// </summary>
public sealed class FurVigenciaExpedienteConsolidadoTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly RecordingRegenerator _regenerador = new();
    private readonly FakeMerger _merger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Worker de regeneración anticipada ───────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_ConsolidadoVigenteConFurAnteriorALaPlaca_NoSeOmite_RegeneraFurYConsolidado()
    {
        var instance = Instancia(furAntesDeLaPlaca: true);
        AgregarConsolidadoVigente(instance, "consolidado");

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Wizard, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Regenerado);
        _regenerador.Llamadas.Should().Be(1, "el FUR se regenera antes de fusionar");
        _storage.Saved.Should().ContainSingle(p => p.Contains("consolidado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Worker_ConsolidadoVigenteConFurPosteriorALaPlaca_SeOmitePorVigente()
    {
        var instance = Instancia(furAntesDeLaPlaca: false);
        AgregarConsolidadoVigente(instance, "consolidado_maestro");

        var resultado = await Worker().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.OmitidoVigente);
        _regenerador.Llamadas.Should().Be(0);
        _storage.Saved.Should().BeEmpty();
    }

    // ── Consolidado maestro (el que ve el OT) ───────────────────────────────────────────────────

    [Fact]
    public async Task Maestro_CorreccionDePlacaPosteriorAlFur_RegeneraElFurAntesDeFusionar()
    {
        var instance = Instancia(furAntesDeLaPlaca: false);
        // La corrección de placa (HU #12167) no regenera documentos: deja el FUR con la placa anterior.
        instance.PlateUpdatedAt = FurDe(instance).UploadedAt.AddMinutes(5);
        AgregarConsolidadoVigente(instance, "consolidado_maestro");

        var (result, error) = await Maestro().HandleAsync(instance.Id, instance.TenantId, ct: Ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue("el maestro vigente se armó con el FUR sin la placa corregida");
        _regenerador.Llamadas.Should().Be(1);
    }

    [Theory] // Bug #13055 (alcance ampliado) — cualquier cambio de datos, no solo la placa.
    [InlineData(TipoConsolidado.Wizard)]
    [InlineData(TipoConsolidado.Maestro)]
    public async Task CambioDeDatosPosteriorAlFur_RegeneraElFurAntesDeFusionar(TipoConsolidado documento)
    {
        var instance = Instancia(furAntesDeLaPlaca: false);
        // p. ej. el gestor corrige la dirección del comprador o el OT un dato del vehículo.
        instance.ExpedienteActualizadoEn = FurDe(instance).UploadedAt.AddMinutes(2);
        AgregarConsolidadoVigente(instance, documento == TipoConsolidado.Wizard ? "consolidado" : "consolidado_maestro");

        var (result, error) = documento == TipoConsolidado.Wizard
            ? await Wizard().HandleAsync(instance.Id, instance.TenantId, Ct)
            : await Maestro().HandleAsync(instance.Id, instance.TenantId, ct: Ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue("el consolidado vigente se armó con el FUR anterior al cambio");
        _regenerador.Llamadas.Should().Be(1);
    }

    [Fact]
    public async Task Maestro_FurCargadoAMano_NoSeConsideraDesactualizado_SirveLaCache()
    {
        var instance = Instancia(furAntesDeLaPlaca: true, furSource: "user");
        AgregarConsolidadoVigente(instance, "consolidado_maestro");

        var (result, _) = await Maestro().HandleAsync(instance.Id, instance.TenantId, ct: Ct);

        result!.Regenerado.Should().BeFalse("la generación nunca reemplaza un FUR cargado por el usuario");
        _regenerador.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Maestro_FalloAlRegenerarElFur_FusionaIgualConElFurAnterior()
    {
        var instance = Instancia(furAntesDeLaPlaca: true);
        _regenerador.Lanzar = true;

        var (result, error) = await Maestro().HandleAsync(instance.Id, instance.TenantId, ct: Ct);

        error.Should().BeNull("el fallo del FUR es best-effort, igual que antes de la corrección");
        result!.Regenerado.Should().BeTrue();
    }

    // ── Consolidado del wizard ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Wizard_ConsolidadoVigenteConFurAnteriorALaPlaca_RegeneraFurYConsolidado()
    {
        var instance = Instancia(furAntesDeLaPlaca: true);
        AgregarConsolidadoVigente(instance, "consolidado");

        var (result, error) = await Wizard().HandleAsync(instance.Id, instance.TenantId, Ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        _regenerador.Llamadas.Should().Be(1);
    }

    [Theory]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    [InlineData(TramiteEstado.Aprobado)]
    public async Task Wizard_EstadoFinal_NoTocaElFur_SirveLaCache(string estado)
    {
        var instance = Instancia(furAntesDeLaPlaca: true);
        instance.Status = estado;
        AgregarConsolidadoVigente(instance, "consolidado");

        var (result, _) = await Wizard().HandleAsync(instance.Id, instance.TenantId, Ct);

        result!.Regenerado.Should().BeFalse("la documentación de un trámite final es definitiva");
        _regenerador.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Wizard_SinPlacaAsignadaPorElOt_SirveLaCache()
    {
        var instance = Instancia(furAntesDeLaPlaca: false);
        instance.PlateAssignedAt = null;
        AgregarConsolidadoVigente(instance, "consolidado");

        var (result, _) = await Wizard().HandleAsync(instance.Id, instance.TenantId, Ct);

        result!.Regenerado.Should().BeFalse();
        _regenerador.Llamadas.Should().Be(0);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private GenerarConsolidadoHandler Wizard() =>
        new(_repo, _merger, _storage, hotDocsRegenerator: _regenerador);

    private GenerarConsolidadoMaestroHandler Maestro() =>
        new(_repo, _merger, _storage, hotDocsRegenerator: _regenerador);

    private RegenerarConsolidadoAnticipadoHandler Worker() => new(_repo, Wizard(), Maestro());

    private ProcedureInstance Instancia(bool furAntesDeLaPlaca, string furSource = "system")
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013055",
            Status = TramiteEstado.Asignado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Adjuntar(instance, "fur", furSource);
        Adjuntar(instance, "factura", "user");
        var fur = FurDe(instance).UploadedAt;
        instance.PlateAssignedAt = furAntesDeLaPlaca ? fur.AddMinutes(1) : fur.AddMinutes(-1);

        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private static ProcedureInstanceAttachment FurDe(ProcedureInstance instance) =>
        instance.Attachments.Single(a => a.Tipo == "fur");

    private void AgregarConsolidadoVigente(ProcedureInstance instance, string tipo)
    {
        Adjuntar(instance, tipo, "system");
        if (tipo == "consolidado") instance.ConsolidadoWizardVigente = true;
        else instance.ConsolidadoMaestroVigente = true;
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

    private sealed class RecordingRegenerator : IExpedienteHotDocumentsRegenerator
    {
        public int Llamadas { get; private set; }
        public bool Lanzar { get; set; }

        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            Llamadas++;
            return Lanzar
                ? throw new InvalidOperationException("fallo simulado del FUR")
                : Task.FromResult<string?>(null);
        }
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
}
