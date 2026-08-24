using Flit.Tramites.Application.Signatures;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class FirmaHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ISignatureProvider _provider = new MockSignatureProvider();
    private readonly SolicitarFirmaHandler _solicitar;
    private readonly ListFirmasHandler _list;
    private readonly SimularFirmaHandler _simular;

    public FirmaHandlerTests()
    {
        _solicitar = new SolicitarFirmaHandler(_repo, _provider);
        _list = new ListFirmasHandler(_repo);
        _simular = new SimularFirmaHandler(_repo);
    }

    private static ProcedureInstance Instance(
        Guid id, Guid tenantId, string tipologia = TramiteTipologiaCatalog.CodigoTraspasoStandard) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(tipologia ?? (tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial")),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = tipologia == TramiteTipologiaCatalog.CodigoTraspasoStandard ? "traspaso" : "matricula_inicial",
            TipologiaCodigo = tipologia,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Solicitar firma ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Solicitar_Traspaso_HappyPath_PersistsEnviada()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _solicitar.HandleAsync(id, tenant, new SolicitarFirmaInput("comprador", null), ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(SignatureEstados.Enviada);
        result.EnvelopeId.Should().NotBeNullOrWhiteSpace();
        result.SignUrl.Should().NotBeNullOrWhiteSpace();
        result.DocTipo.Should().Be(SignatureDocTipos.Compraventa);
        instance.Signatures.Should().ContainSingle();
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceSignature>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Solicitar_Matricula_NotApplicable_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct)
            .Returns(Instance(id, tenant, tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial));

        var (_, error) = await _solicitar.HandleAsync(id, tenant, new SolicitarFirmaInput("comprador", null), ct);

        error.Should().Be("no_aplica");
    }

    [Fact]
    public async Task Solicitar_InvalidParte_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, error) = await _solicitar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), new SolicitarFirmaInput("tercero", null), ct);

        error.Should().Be("parte_invalida");
    }

    [Fact]
    public async Task Solicitar_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithFurGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _solicitar.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), new SolicitarFirmaInput("comprador", null), ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Solicitar_Idempotent_ReusesExistingActiveSignature()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var existing = new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Parte = "comprador",
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = SignatureEstados.Enviada,
            EnvelopeId = "env-1",
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Signatures.Add(existing);
        _repo.GetByIdWithFurGraphAsync(id, tenant, ct).Returns(instance);

        var (result, error) = await _solicitar.HandleAsync(id, tenant, new SolicitarFirmaInput("comprador", null), ct);

        error.Should().BeNull();
        result!.Id.Should().Be(existing.Id);
        instance.Signatures.Should().ContainSingle();
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceSignature>());
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    // ── Simular firma ──────────────────────────────────────────────────────────

    private (ProcedureInstance instance, ProcedureInstanceSignature sig) SeedEnviada(string estado = SignatureEstados.Enviada)
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        var sig = new ProcedureInstanceSignature
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            ProcedureInstanceId = id,
            Parte = "comprador",
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = estado,
            EnvelopeId = "env-1",
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Signatures.Add(sig);
        _repo.GetByIdWithSignaturesAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);
        return (instance, sig);
    }

    [Fact]
    public async Task Simular_FromEnviada_CompletesToFirmada()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, sig) = SeedEnviada();

        var (result, error) = await _simular.HandleAsync(instance.Id, instance.TenantId, sig.Id, ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(SignatureEstados.Firmada);
        result.PdfPath.Should().NotBeNullOrWhiteSpace();
        result.Sha256.Should().NotBeNullOrWhiteSpace();
        sig.Estado.Should().Be(SignatureEstados.Firmada);
        sig.FirmadoAt.Should().NotBeNull();
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Simular_AlreadyFirmada_Idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, sig) = SeedEnviada(estado: SignatureEstados.Firmada);
        sig.PdfPath = "p"; sig.Sha256 = "h";

        var (result, error) = await _simular.HandleAsync(instance.Id, instance.TenantId, sig.Id, ct);

        error.Should().BeNull();
        result!.Estado.Should().Be(SignatureEstados.Firmada);
        await _repo.DidNotReceive().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Simular_SignatureNotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instance, _) = SeedEnviada();

        var (_, error) = await _simular.HandleAsync(instance.Id, instance.TenantId, Guid.NewGuid(), ct);

        error.Should().Be("signature_not_found");
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithSignaturesAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _list.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
    }

    // ── Preflight firma check (DerivaFirmaCompraventaCheck) ──────────────────────

    [Fact]
    public void PreflightFirmaCheck_Matricula_ReturnsNull()
    {
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid(), tipologia: TramiteTipologiaCatalog.CodigoMatriculaInicial);

        GetWizardStateHandler.DerivaFirmaCompraventaCheck(instance).Should().BeNull();
    }

    [Fact]
    public void PreflightFirmaCheck_BothFirmadas_Green()
    {
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.Signatures.Add(Sig("comprador", SignatureEstados.Firmada));
        instance.Signatures.Add(Sig("vendedor", SignatureEstados.Firmada));

        GetWizardStateHandler.DerivaFirmaCompraventaCheck(instance)!.Status.Should().Be("green");
    }

    [Fact]
    public void PreflightFirmaCheck_AnyRechazada_Fail()
    {
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.Signatures.Add(Sig("comprador", SignatureEstados.Firmada));
        instance.Signatures.Add(Sig("vendedor", SignatureEstados.Rechazada));

        GetWizardStateHandler.DerivaFirmaCompraventaCheck(instance)!.Status.Should().Be("fail");
    }

    [Fact]
    public void PreflightFirmaCheck_Pending_Warn()
    {
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.Signatures.Add(Sig("comprador", SignatureEstados.Enviada));

        GetWizardStateHandler.DerivaFirmaCompraventaCheck(instance)!.Status.Should().Be("warn");
    }

    private static ProcedureInstanceSignature Sig(string parte, string estado) =>
        new()
        {
            Id = Guid.NewGuid(),
            Parte = parte,
            DocTipo = SignatureDocTipos.Compraventa,
            Estado = estado,
            SolicitadoAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}

public sealed class MockSignatureProviderTests
{
    [Fact]
    public void CreateEnvelope_ReturnsEnvelopeAndSignUrl()
    {
        var provider = new MockSignatureProvider();
        var id = Guid.NewGuid();

        var envelope = provider.CreateEnvelope(new SignatureRequest(id, "comprador", "compraventa", "Juan"));

        envelope.EnvelopeId.Should().Contain(id.ToString("N"));
        envelope.EnvelopeId.Should().Contain("comprador");
        envelope.SignUrl.Should().Be($"/sign/{envelope.EnvelopeId}");
    }
}
