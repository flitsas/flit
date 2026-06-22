using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class CommercialHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly PutCommercialHandler _put;
    private readonly GetCommercialHandler _get;

    public CommercialHandlerTests()
    {
        _put = new PutCommercialHandler(_repo);
        _get = new GetCommercialHandler(_repo);
    }

    private static ProcedureInstance Instance(
        string status = ProcedureInstanceStatus.Draft,
        ProcedureInstanceCommercial? commercial = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            ModalidadEntrada = "traspaso",
            Commercial = commercial,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static CommercialDto Valid(decimal valor = 50_000_000m, string causal = "COMPRAVENTA") =>
        new(valor, causal, 0.01m, 120_000m, "transferencia");

    // ── 404 / 409 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_InstanceNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns((ProcedureInstance?)null);

        var (result, error) = await _put.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), Valid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("submitted")]
    [InlineData("completed")]
    public async Task Put_NotDraft_ReturnsConflict(string status)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(status: status);
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (_, error) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(), ct);

        error.Should().Be("not_draft");
    }

    // ── Validación ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Put_ValorVentaNotPositive_ReturnsInvalid(decimal valor)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (_, error) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(valor: valor), ct);

        error.Should().Be("invalid_valor_venta");
    }

    [Theory]
    [InlineData("COMPRAVENTA")]
    [InlineData("DONACION")]
    [InlineData("DACION_EN_PAGO")]
    [InlineData("ADJUDICACION")]
    public async Task Put_ValidCausal_Succeeds(string causal)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(causal: causal), ct);

        error.Should().BeNull();
        result!.Causal.Should().Be(causal);
    }

    [Theory]
    [InlineData("VENTA")]
    [InlineData("")]
    [InlineData("REMATE")]
    public async Task Put_InvalidCausal_ReturnsInvalid(string causal)
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance();
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (_, error) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(causal: causal), ct);

        error.Should().Be("invalid_causal");
    }

    // ── Persistencia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_NewCommercial_CreatesAndPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(commercial: null);
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, error) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(valor: 99m), ct);

        error.Should().BeNull();
        result!.ValorVenta.Should().Be(99m);
        instance.Commercial.Should().NotBeNull();
        instance.Commercial!.ValorVenta.Should().Be(99m);
        // El comercial NUEVO (1:1) se marca Added explícito → INSERT (PK store-generated con Id ya seteado).
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceCommercial>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Put_ExistingCommercial_UpdatesInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            ValorVenta = 10m,
            Causal = "DONACION",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var instance = Instance(commercial: existing);
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, _) = await _put.HandleAsync(instance.Id, instance.TenantId, Valid(valor: 77m, causal: "COMPRAVENTA"), ct);

        result!.ValorVenta.Should().Be(77m);
        instance.Commercial!.Id.Should().Be(existing.Id); // misma fila (update, no insert).
        instance.Commercial.Causal.Should().Be("COMPRAVENTA");
        instance.Commercial.UpdatedAt.Should().NotBeNull();
        // Actualizar el comercial EXISTENTE (ya trackeado) NO marca Added: queda como UPDATE.
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceCommercial>());
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_NoCommercial_ReturnsNullResultNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(commercial: null);
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, error) = await _get.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_WithCommercial_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instance(commercial: new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            ValorVenta = 42m,
            Causal = "ADJUDICACION",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithCommercialAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct).Returns(instance);

        var (result, error) = await _get.HandleAsync(instance.Id, instance.TenantId, ct);

        error.Should().BeNull();
        result!.ValorVenta.Should().Be(42m);
        result.Causal.Should().Be("ADJUDICACION");
    }
}
