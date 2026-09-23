using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12784 (Épica #12760) — cambiar el firmante del mandato invalida los consolidados (wizard y
/// maestro), porque el mandato que va dentro de ambos lleva impreso a quien firma.
///
/// <para>Uso de ejemplo:
/// <code>
/// var error = await new SetMandateSignerHandler(repo, directorio)
///     .HandleAsync(instanceId, tenantId, nuevoMandatarioId, ct);
/// // error == null  ⇒ instance.ConsolidadoWizardVigente == false y ConsolidadoMaestroVigente == false
/// </code></para>
/// </summary>
public sealed class SetMandateSignerInvalidaConsolidadosTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-2222-4000-8000-000000000001");
    private static readonly Guid Ot = Guid.Parse("bbbbbbbb-2222-4000-8000-000000000001");
    private static readonly Guid Ana = Guid.Parse("cccccccc-2222-4000-8000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("cccccccc-2222-4000-8000-000000000002");

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    private sealed class Directorio(params MandateSignerCandidate[] candidatos) : IMandateSignerDirectory
    {
        public Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
            Guid transitOfficeId, Guid companyTenantId, string? nitMandante = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MandateSignerCandidate>>(
                transitOfficeId == Ot && companyTenantId == Tenant ? candidatos : []);

        public Task<MandateSignerCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(candidatos.FirstOrDefault(c => c.Id == id));
    }

    private static MandateSignerCandidate Candidato(Guid id, string nombre) =>
        new(id, nombre, "1020304050", null, true, null, "CC", null, null);

    private static Directorio AnaYCarlos() =>
        new(Candidato(Ana, "Ana Restrepo"), Candidato(Carlos, "Carlos Pérez"));

    /// <summary>Trámite con ambos consolidados vigentes y el organismo en field_values (como en el wizard).</summary>
    private ProcedureInstance InstanciaConConsolidadosVigentes(
        string estado = TramiteEstado.Borrador, Guid? elegido = null, bool subsanacionActiva = false)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "MAT-2026-012784",
            Status = estado,
            SubsanacionActiva = subsanacionActiva,
            MandateSignerId = elegido,
            ConsolidadoWizardVigente = true,
            ConsolidadoMaestroVigente = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            FieldKey = "transit_office_id",
            ValueText = Ot.ToString(),
            Source = "user",
        });

        _repo.GetByIdWithDetailsAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    // ── AC1 — firmante distinto invalida ambos consolidados ───────────────────

    [Fact]
    public async Task AC1_EnBorrador_FirmanteDistinto_InvalidaWizardYMaestro()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(elegido: Ana);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos())
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().BeNull();
        instance.MandateSignerId.Should().Be(Carlos);
        instance.ConsolidadoWizardVigente.Should().BeFalse();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task AC1_PrimeraEleccion_DesdeSinFirmante_TambienInvalida()
    {
        // null → Ana también es un cambio: el consolidado previo se armó con el firmante por default.
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(elegido: null);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos())
            .HandleAsync(instance.Id, Tenant, Ana, ct);

        error.Should().BeNull();
        instance.ConsolidadoWizardVigente.Should().BeFalse();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task AC1_EnSubsanacionActiva_FirmanteDistinto_TambienInvalida()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(
            estado: TramiteEstado.Rechazado, elegido: Ana, subsanacionActiva: true);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos())
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().BeNull();
        instance.ConsolidadoWizardVigente.Should().BeFalse();
        instance.ConsolidadoMaestroVigente.Should().BeFalse();
    }

    [Fact]
    public async Task AC1_MandatarioNoHabilitado_NoInvalidaNiEscribe()
    {
        // Contrato: si el cambio se rechaza, el expediente no cambió y los consolidados siguen vigentes.
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(elegido: Ana);

        var error = await new SetMandateSignerHandler(_repo, new Directorio(Candidato(Ana, "Ana Restrepo")))
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().Be("mandatario_no_habilitado");
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        instance.ConsolidadoMaestroVigente.Should().BeTrue();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    // ── AC2 — mismo firmante: sin invalidación ni escritura ───────────────────

    [Fact]
    public async Task AC2_MismoFirmante_BanderasSiguenVigentesYSinEscritura()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(elegido: Ana);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos())
            .HandleAsync(instance.Id, Tenant, Ana, ct);

        error.Should().BeNull();
        instance.MandateSignerId.Should().Be(Ana);
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        instance.ConsolidadoMaestroVigente.Should().BeTrue();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    // ── AC3 — estados finales: la documentación definitiva no se altera ──────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Rechazado)] // rechazado SIN subsanación activa
    [InlineData(TramiteEstado.Anulado)]
    public async Task AC3_FueraDeBorrador_CambioDeFirmante_NoInvalidaConsolidados(string estado)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = InstanciaConConsolidadosVigentes(estado: estado, elegido: Ana);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos())
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().Be("not_draft");
        instance.MandateSignerId.Should().Be(Ana);
        instance.ConsolidadoWizardVigente.Should().BeTrue();
        instance.ConsolidadoMaestroVigente.Should().BeTrue();
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync(ct);
    }

    // ── AC1 (completo) — el mandato con el firmante anterior no se reutiliza ──────────────────

    private sealed class Storage : IAttachmentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Deleted { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{Guid.NewGuid():N}";
            Files[path] = ms.ToArray();
            return new StoredFile(path, $"sha-{tipo}", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath)
        {
            Deleted.Add(storagePath);
            Files.Remove(storagePath);
        }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var b) ? new MemoryStream(b) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    /// <summary>Concatena las partes: el PDF resultante deja leer qué mandato entró al consolidado.</summary>
    private sealed class Merger : IExpedienteConsolidadoMerger
    {
        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;
        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();
        public byte[] Compose(MergeRequest request) => Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    /// <summary>
    /// Doble de GenerarFurHandler: regenera el mandato con el firmante que tenga el trámite EN ESE
    /// MOMENTO (igual que el generador real, que lo lee de instance.MandateSignerId).
    /// </summary>
    private sealed class MandatoRegenerator(ProcedureInstance instance, Storage storage) : IExpedienteHotDocumentsRegenerator
    {
        public int Calls { get; private set; }

        public Task<string?> RegenerateHotDocumentsAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            Adjuntar(instance, storage, "mandato", $"[mandato:{instance.MandateSignerId}]", "system");
            return Task.FromResult<string?>(null);
        }
    }

    private static void Adjuntar(ProcedureInstance instance, Storage storage, string tipo, string contenido, string source)
    {
        var path = $"{instance.Id:D}/{tipo}_{Guid.NewGuid():N}";
        storage.Files[path] = Encoding.UTF8.GetBytes(contenido);
        instance.Attachments.Add(new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = $"{tipo}.pdf",
            Mimetype = "application/pdf",
            SizeBytes = contenido.Length,
            Sha256 = $"sha-{tipo}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        });
    }

    private static void QuitarMandato(ProcedureInstance instance)
    {
        foreach (var a in instance.Attachments.Where(a => a.Tipo == "mandato").ToList())
            instance.Attachments.Remove(a);
    }

    /// <summary>Expediente con FUR ya persistido, mandato de Ana y consolidado vigente.</summary>
    private (ProcedureInstance Instance, Storage Storage) ExpedienteConFurYMandatoDeAna(
        string mandatoSource = "system", string estado = TramiteEstado.Borrador)
    {
        var instance = InstanciaConConsolidadosVigentes(estado: estado, elegido: Ana);
        var storage = new Storage();
        Adjuntar(instance, storage, "factura", "[factura]", "user");
        Adjuntar(instance, storage, "fur", "[fur]", "system");
        Adjuntar(instance, storage, "mandato", $"[mandato:{Ana}]", mandatoSource);
        Adjuntar(instance, storage, "consolidado", "[consolidado-previo]", "system");
        _repo.GetByIdWithAttachmentsAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, Tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return (instance, storage);
    }

    private static string Contenido(Storage storage, GenerarConsolidadoResult result, ProcedureInstance instance)
    {
        var adjunto = instance.Attachments.Single(a => a.Id == result.Document.AttachmentId);
        return Encoding.UTF8.GetString(storage.Files[adjunto.StoragePath]);
    }

    [Fact]
    public async Task AC1_ConFurExistente_SiguienteConsolidadoRegeneraElMandatoConElNuevoFirmante()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, storage) = ExpedienteConFurYMandatoDeAna();
        var mandatoViejo = instance.Attachments.Single(a => a.Tipo == "mandato");
        var regenerador = new MandatoRegenerator(instance, storage);

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos(), storage)
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        // El PUT NO regenera en caliente: solo retira el mandato generado con Ana.
        error.Should().BeNull();
        regenerador.Calls.Should().Be(0);
        storage.Deleted.Should().Contain(mandatoViejo.StoragePath);
        instance.Attachments.Should().NotContain(a => a.Tipo == "mandato");
        _repo.Received(1).RemoveAttachment(mandatoViejo);

        var (result, errorConsolidado) = await new GenerarConsolidadoHandler(
                _repo, new Merger(), storage, hotDocsRegenerator: regenerador)
            .HandleAsync(instance.Id, Tenant, ct);

        errorConsolidado.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        regenerador.Calls.Should().Be(1, "sin mandato el consolidado pide regenerar los documentos en caliente");
        var pdf = Contenido(storage, result, instance);
        pdf.Should().Contain($"[mandato:{Carlos}]");
        pdf.Should().NotContain($"[mandato:{Ana}]");
    }

    [Fact]
    public async Task AC1_MandatoPersonalizadoDeLaCompania_NoSeRetira()
    {
        // DT-3 (HU #11316): solo se retira lo que generó el sistema; el mandato de la compañía
        // sobrevive al cambio de firmante.
        var ct = TestContext.Current.CancellationToken;
        var (instance, storage) = ExpedienteConFurYMandatoDeAna(mandatoSource: "company");

        var error = await new SetMandateSignerHandler(_repo, AnaYCarlos(), storage)
            .HandleAsync(instance.Id, Tenant, Carlos, ct);

        error.Should().BeNull();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato" && a.Source == "company");
        storage.Deleted.Should().BeEmpty();
        instance.ConsolidadoWizardVigente.Should().BeFalse();
    }

    [Fact]
    public async Task AC2_MismoFirmante_ConsolidadoSeSirveCacheadoSinRegenerarMandato()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, storage) = ExpedienteConFurYMandatoDeAna();
        var regenerador = new MandatoRegenerator(instance, storage);

        await new SetMandateSignerHandler(_repo, AnaYCarlos(), storage).HandleAsync(instance.Id, Tenant, Ana, ct);
        var (result, _) = await new GenerarConsolidadoHandler(
                _repo, new Merger(), storage, hotDocsRegenerator: regenerador)
            .HandleAsync(instance.Id, Tenant, ct);

        storage.Deleted.Should().BeEmpty();
        instance.Attachments.Should().ContainSingle(a => a.Tipo == "mandato");
        regenerador.Calls.Should().Be(0);
        result!.Regenerado.Should().BeFalse();
    }

    [Fact]
    public async Task AC3_FueraDeBorrador_SinMandato_ElConsolidadoNoDisparaLaCascada()
    {
        // Un trámite final con firmante y sin adjunto mandato NO regenera FUR ni mandato por esta
        // vía: la documentación definitiva no se altera.
        var ct = TestContext.Current.CancellationToken;
        var (instance, storage) = ExpedienteConFurYMandatoDeAna(estado: TramiteEstado.Aprobado);
        QuitarMandato(instance);
        instance.InvalidarConsolidados();
        var regenerador = new MandatoRegenerator(instance, storage);

        await new GenerarConsolidadoHandler(_repo, new Merger(), storage, hotDocsRegenerator: regenerador)
            .HandleAsync(instance.Id, Tenant, ct);

        regenerador.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Regresion11066_SinFirmanteElegido_ElConsolidadoInvalidadoSoloFusiona()
    {
        // Sin mandatario elegido la ausencia de mandato es legítima (no aplica): se conserva el
        // comportamiento de Feature #11066 — con FUR en pie, solo fusiona.
        var ct = TestContext.Current.CancellationToken;
        var (instance, storage) = ExpedienteConFurYMandatoDeAna();
        instance.MandateSignerId = null;
        QuitarMandato(instance);
        instance.InvalidarConsolidados();
        var regenerador = new MandatoRegenerator(instance, storage);

        var (result, _) = await new GenerarConsolidadoHandler(
                _repo, new Merger(), storage, hotDocsRegenerator: regenerador)
            .HandleAsync(instance.Id, Tenant, ct);

        regenerador.Calls.Should().Be(0);
        result!.Regenerado.Should().BeTrue();
    }
}
