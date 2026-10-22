using System.Text;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13265 (Feature #13261, Épica #12741) — «gana quien carga primero»: si FLITO (adjunto con provider = flito) cargó el
/// liquidacion_impuesto, el gestor no lo reemplaza (subida, presign, registro) ni lo borra.
/// </summary>
public sealed class BloqueoGestorComprobanteFlitoHu13265Tests
{
    private const string Tipo = ExternalAttachmentRules.LiquidacionImpuesto;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly Storage _storage = new();
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private sealed class Storage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public List<string> Presigned { get; } = [];
        public List<string> Deleted { get; } = [];

        public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_{originalFilename}";
            Saved.Add(path);
            return new StoredFile(path, "deadbeef", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default)
        {
            Presigned.Add(tipo);
            return Task.FromResult(new PresignedUpload("p", "https://s3.test/upload", new Dictionary<string, string>()));
        }

        public void Delete(string storagePath) => Deleted.Add(storagePath);
        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }

    private static AttachmentValidator Validator()
    {
        var catalog = Substitute.For<IDocumentTypeCatalog>();
        catalog.GetRuleAsync(Tipo, Arg.Any<CancellationToken>())
            .Returns(new DocumentTypeRule(Tipo, ["application/pdf"], 20L * 1024 * 1024));
        return new AttachmentValidator(catalog);
    }

    private ProcedureInstance Instance(params ProcedureInstanceAttachment[] adjuntos)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = _id,
            TenantId = _tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ChecklistEstado = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var a in adjuntos)
            instance.Attachments.Add(a);
        _repo.GetByIdWithAttachmentsAsync(_id, _tenant, _ct).Returns(instance);
        return instance;
    }

    private ProcedureInstanceAttachment Adjunto(string tipo, string? provider, string source = "user", bool historico = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProcedureInstanceId = _id,
            Tipo = tipo,
            Provider = provider,
            Source = source,
            IsHistorico = historico,
            StoragePath = $"path/{tipo}_{provider ?? "gestor"}.pdf",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    private ProcedureInstanceAttachment DeFlito(string tipo = Tipo) => Adjunto(tipo, ExternalAttachmentRules.Provider);

    private static UploadAttachmentInput Subida(string tipo = Tipo) =>
        new(tipo, "gestor.pdf", "application/pdf", 1024, new MemoryStream(Encoding.UTF8.GetBytes("pdf")));

    private static PresignAttachmentInput PresignIn(string tipo = Tipo) => new(tipo, "gestor.pdf", "application/pdf", 1024);

    private static RegisterAttachmentInput RegistroIn(string tipo = Tipo) =>
        new(tipo, "gestor.pdf", "application/pdf", 1024, "abc123", "file_xyz");

    // AC1 — subida multipart bloqueada

    [Fact]
    public async Task AC1_SubidaMultipart_ConComprobanteDeFlito_409_YConservaFilaYBinario()
    {
        var flito = DeFlito();
        var instance = Instance(flito);
        var handler = new UploadAttachmentHandler(_repo, _storage, Validator());

        var (result, error) = await handler.HandleAsync(_id, _tenant, Subida(), null, _ct);

        result.Should().BeNull();
        error.Should().Be("adjunto_bloqueado_flito");
        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(flito);
        _storage.Saved.Should().BeEmpty();
        _storage.Deleted.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_SubidaMultipart_TipoEnMayusculas_TambienBloquea()
    {
        Instance(DeFlito("LIQUIDACION_IMPUESTO"));
        var handler = new UploadAttachmentHandler(_repo, _storage, Validator());

        var (_, error) = await handler.HandleAsync(_id, _tenant, Subida(" liquidacion_impuesto "), null, _ct);

        error.Should().Be(ExternalAttachmentRules.BlockedCode);
    }

    // AC2 — presign y registro bloqueados

    [Fact]
    public async Task AC2_Presign_ConComprobanteDeFlito_409_AntesDeEmitirLaUrl()
    {
        Instance(DeFlito());
        var handler = new PresignAttachmentHandler(_repo, _storage, Validator());

        var (result, error) = await handler.HandleAsync(_id, _tenant, PresignIn(), _ct);

        result.Should().BeNull();
        error.Should().Be("adjunto_bloqueado_flito");
        _storage.Presigned.Should().BeEmpty();
    }

    [Fact]
    public async Task AC2_Registro_ConComprobanteDeFlito_409_SinRetirarElDeFlito()
    {
        var flito = DeFlito();
        var instance = Instance(flito);
        var handler = new RegisterAttachmentHandler(_repo, _storage, Validator());

        var (result, error) = await handler.HandleAsync(_id, _tenant, RegistroIn(), null, _ct);

        result.Should().BeNull();
        error.Should().Be("adjunto_bloqueado_flito");
        instance.Attachments.Should().ContainSingle().Which.Should().BeSameAs(flito);
        _storage.Deleted.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // AC3 — borrado bloqueado

    [Fact]
    public async Task AC3_Borrado_DeAdjuntoDeFlito_AdjuntoProtegido_YSeConserva()
    {
        var flito = DeFlito();
        var instance = Instance(flito);
        var handler = new DeleteAttachmentHandler(_repo, _storage);

        var error = await handler.HandleAsync(_id, _tenant, flito.Id, _ct);

        error.Should().Be("adjunto_protegido");
        instance.Attachments.Should().ContainSingle();
        _storage.Deleted.Should().BeEmpty();
        _repo.DidNotReceive().RemoveAttachment(Arg.Any<ProcedureInstanceAttachment>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // AC4 — sin cambio para lo que no es de FLITO

    [Fact]
    public async Task AC4_SinAdjuntoDeFlito_ElGestorSigueReemplazandoYBorrando()
    {
        var propio = Adjunto(Tipo, provider: null);
        var instance = Instance(propio);

        var (result, error) = await new UploadAttachmentHandler(_repo, _storage, Validator())
            .HandleAsync(_id, _tenant, Subida(), null, _ct);

        error.Should().BeNull();
        result!.Tipo.Should().Be(Tipo);
        instance.Attachments.Should().ContainSingle().Which.Id.Should().Be(result.Id);
        _storage.Deleted.Should().ContainSingle().Which.Should().Be(propio.StoragePath);

        var error2 = await new DeleteAttachmentHandler(_repo, _storage).HandleAsync(_id, _tenant, result.Id, _ct);
        error2.Should().BeNull();
        instance.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_OtroTipo_EnTramiteConComprobanteDeFlito_NoSeAfecta()
    {
        var flito = DeFlito();
        var factura = Adjunto("factura", provider: null);
        var instance = Instance(flito, factura);

        var (result, error) = await new UploadAttachmentHandler(_repo, _storage)
            .HandleAsync(_id, _tenant, Subida("factura"), null, _ct);
        error.Should().BeNull();
        instance.Attachments.Should().Contain(flito).And.NotContain(factura).And.Contain(a => a.Id == result!.Id);

        var (_, presignError) = await new PresignAttachmentHandler(_repo, _storage).HandleAsync(_id, _tenant, PresignIn("factura"), _ct);
        presignError.Should().BeNull();

        var (registrado, regError) = await new RegisterAttachmentHandler(_repo, _storage).HandleAsync(_id, _tenant, RegistroIn("factura"), null, _ct);
        regError.Should().BeNull();

        var delError = await new DeleteAttachmentHandler(_repo, _storage).HandleAsync(_id, _tenant, registrado!.Id, _ct);
        delError.Should().BeNull();
        instance.Attachments.Should().Contain(flito);
    }

    [Fact]
    public async Task AC4_ComprobanteDeFlitoHistorico_NoBloqueaLaSubidaDelGestor()
    {
        // Vigente = no histórico (p. ej. tras una revocación el FUR/adjuntos quedan históricos).
        Instance(Adjunto(Tipo, ExternalAttachmentRules.Provider, historico: true));

        var (_, error) = await new UploadAttachmentHandler(_repo, _storage, Validator())
            .HandleAsync(_id, _tenant, Subida(), null, _ct);

        error.Should().BeNull();
    }

    [Fact]
    public async Task AC4_TipoMultiple_ConAdjuntoDeFlito_NoReemplazaPorLoTantoNoBloquea()
    {
        var flito = DeFlito("otro");
        var instance = Instance(flito);

        var (_, error) = await new UploadAttachmentHandler(_repo, _storage)
            .HandleAsync(_id, _tenant, Subida("otro"), null, _ct);

        error.Should().BeNull();
        instance.Attachments.Should().Contain(flito).And.HaveCount(2);
    }

    // Carrera con FLITO: el motor (DDL 131) rechaza el insert aunque la lectura previa no viera el adjunto de FLITO.

    private void ElMotorRechazaElGuardado()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("23505")));
        _repo.IsFlitoFirstWinsConflict(Arg.Any<Exception>()).Returns(true);
    }

    [Fact]
    public async Task AC1_Carrera_SubidaMultipart_SiElMotorRechaza_409_RetiraElBinarioNuevo_YNoTocaLosPropios()
    {
        var propio = Adjunto(Tipo, provider: null);
        Instance(propio);
        ElMotorRechazaElGuardado();

        var (result, error) = await new UploadAttachmentHandler(_repo, _storage, Validator())
            .HandleAsync(_id, _tenant, Subida(), null, _ct);

        result.Should().BeNull();
        error.Should().Be(ExternalAttachmentRules.BlockedCode);
        _storage.Deleted.Should().ContainSingle().Which.Should().Be(_storage.Saved.Single(), "solo el binario recién subido");
        _storage.Deleted.Should().NotContain(propio.StoragePath, "la fila del gestor sigue en BD: su binario no se borra antes de confirmar");
    }

    [Fact]
    public async Task AC2_Carrera_Registro_SiElMotorRechaza_409_SinBorrarNingunBinario()
    {
        var propio = Adjunto(Tipo, provider: null);
        Instance(propio);
        ElMotorRechazaElGuardado();

        var (result, error) = await new RegisterAttachmentHandler(_repo, _storage, Validator())
            .HandleAsync(_id, _tenant, RegistroIn(), null, _ct);

        result.Should().BeNull();
        error.Should().Be(ExternalAttachmentRules.BlockedCode);
        _storage.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Carrera_UnErrorQueNoEsDeGanaElPrimero_SePropaga()
    {
        Instance();
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("otro")));

        var act = async () => await new UploadAttachmentHandler(_repo, _storage, Validator())
            .HandleAsync(_id, _tenant, Subida(), null, _ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
