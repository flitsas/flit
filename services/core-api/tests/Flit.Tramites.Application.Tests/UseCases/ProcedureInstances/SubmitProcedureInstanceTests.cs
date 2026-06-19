using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class SubmitProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly SubmitProcedureInstanceHandler _sut;

    public SubmitProcedureInstanceTests()
    {
        _sut = new SubmitProcedureInstanceHandler(_repo, _typeRepo);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenantId, string status) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ProcedureType PublishedType(Guid id) =>
        new()
        {
            Id = id,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_AlreadySubmitted_ReturnsNotDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct)
            .Returns(Instance(id, tenantId, ProcedureInstanceStatus.Submitted));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().Be("not_draft");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_DraftAndPublished_TransitionsToSubmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var instance = Instance(id, tenantId, ProcedureInstanceStatus.Draft);
        _repo.GetByIdWithDetailsAsync(id, tenantId, ct).Returns(instance);
        _typeRepo.GetByIdAsync(instance.ProcedureTypeId, ct).Returns(PublishedType(instance.ProcedureTypeId));

        var (result, error) = await _sut.HandleAsync(id, tenantId, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ProcedureInstanceStatus.Submitted);
        result.SubmittedAt.Should().NotBeNull();
        instance.Status.Should().Be(ProcedureInstanceStatus.Submitted);
        instance.SubmittedAt.Should().NotBeNull();
        instance.StatusHistory.Should().ContainSingle(h =>
            h.FromStatus == ProcedureInstanceStatus.Draft && h.ToStatus == ProcedureInstanceStatus.Submitted);
        // El status_history NUEVO se marca Added explícito → INSERT (PK store-generated con Id ya seteado).
        _repo.Received(1).Add(Arg.Any<ProcedureInstanceStatusHistory>());
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
