using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class SetImprontaDiferidaHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly SetImprontaDiferidaHandler _handler;

    public SetImprontaDiferidaHandlerTests() => _handler = new SetImprontaDiferidaHandler(_repo);

    private static ProcedureInstance Instance(Guid id, Guid tenantId) =>
        new()
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteTipologiaCatalog.CodigoMatriculaInicial ?? "matricula_inicial"),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private void SetupRepo(ProcedureInstance instance) =>
        _repo.GetByIdWithWizardGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(instance);

    [Fact]
    public async Task Diferir_MarcaImprontaEnChecklistEstado_YGuarda()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        SetupRepo(instance);

        var (ok, error) = await _handler.HandleAsync(instance.Id, instance.TenantId, diferida: true, ct);

        error.Should().BeNull();
        ok.Should().BeTrue();
        instance.ChecklistEstado.Should().Contain("\"impronta\":true");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoDiferir_QuitaLaMarcaPrevia()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.ChecklistEstado = "{\"impronta\":true}";
        SetupRepo(instance);

        var (ok, error) = await _handler.HandleAsync(instance.Id, instance.TenantId, diferida: false, ct);

        error.Should().BeNull();
        ok.Should().BeTrue();
        instance.ChecklistEstado.Should().NotContain("impronta");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Diferir_Idempotente_NoReguardaSiYaEstabaMarcada()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.ChecklistEstado = "{\"impronta\":true}";
        SetupRepo(instance);

        var (ok, error) = await _handler.HandleAsync(instance.Id, instance.TenantId, diferida: true, ct);

        error.Should().BeNull();
        ok.Should().BeTrue();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithWizardGraphAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (_, error) = await _handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), diferida: true, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task NotDraft_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        instance.Status = TramiteEstado.Preparado;
        SetupRepo(instance);

        var (_, error) = await _handler.HandleAsync(instance.Id, instance.TenantId, diferida: true, ct);

        error.Should().Be("not_draft");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Diferir_NoDebilitaRadicacion_SubmitGateSigueExigiendoImprontaReal()
    {
        // Regresión clave: aunque la impronta quede "diferida" (flag manual en checklist_estado), el
        // gate de radicación borrador→preparado sigue exigiendo el attachment REAL de impronta —
        // SubmitGate.ImprontaGenerada ignora el flag manual.
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(Guid.NewGuid(), Guid.NewGuid());
        SetupRepo(instance);

        await _handler.HandleAsync(instance.Id, instance.TenantId, diferida: true, ct);

        instance.ChecklistEstado.Should().Contain("\"impronta\":true");
        // Sin adjunto de impronta, la radicación sigue bloqueada por impronta_requerida.
        instance.Attachments.Should().NotContain(a => a.Tipo == "impronta");
        var errors = SubmitGate.Evaluate(instance, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        errors.Should().Contain(SubmitGate.ImprontaRequerida);
    }
}
