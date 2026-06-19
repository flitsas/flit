using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class CreateProcedureInstanceTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly CreateProcedureInstanceHandler _sut;

    public CreateProcedureInstanceTests()
    {
        _sut = new CreateProcedureInstanceHandler(_repo, _typeRepo);
    }

    private static CreateProcedureInstanceRequest Request() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

    [Fact]
    public async Task HandleAsync_ProcedureTypeNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _typeRepo.GetByIdAsync(Arg.Any<Guid>(), ct).Returns((ProcedureType?)null);

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ProcedureTypeNotPublished_ReturnsNotPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _typeRepo.GetByIdAsync(Arg.Any<Guid>(), ct).Returns(pt);

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().Be("not_published");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Published_CreatesInstanceWithReferenceAndDraftStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _typeRepo.GetByIdAsync(Arg.Any<Guid>(), ct).Returns(pt);
        _repo.CountByTenantAndYearAsync(Arg.Any<Guid>(), Arg.Any<int>(), ct).Returns(0);

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var year = DateTimeOffset.UtcNow.Year;
        result!.ReferenceNumber.Should().Be($"TRM-{year}-000001");
        result.Status.Should().Be(ProcedureInstanceStatus.Draft);

        await _repo.Received(1).AddAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.Status == ProcedureInstanceStatus.Draft &&
                i.StatusHistory.Any(h => h.ToStatus == ProcedureInstanceStatus.Draft && h.FromStatus == null)),
            ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
