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

    /// <summary>Simula el repo real: genera la referencia con seq inicial y persiste OK.</summary>
    private void StubReferenceGenerator(int seq = 1)
    {
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var instance = call.Arg<ProcedureInstance>();
                var year = call.ArgAt<int>(1);
                instance.ReferenceNumber = $"TRM-{year}-{seq:D6}";
                return Task.FromResult(true);
            });
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
        StubReferenceGenerator();

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        var year = DateTimeOffset.UtcNow.Year;
        result!.ReferenceNumber.Should().Be($"TRM-{year}-000001");
        result.Status.Should().Be(ProcedureInstanceStatus.Draft);

        await _repo.Received(1).AddWithUniqueReferenceAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.Status == ProcedureInstanceStatus.Draft &&
                i.StatusHistory.Any(h => h.ToStatus == ProcedureInstanceStatus.Draft && h.FromStatus == null)),
            year,
            ct);
    }

    [Fact]
    public async Task HandleAsync_ReferenceConflictExhausted_ReturnsReferenceConflict()
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
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().Be("reference_conflict");
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("TRASPASO", "traspaso", "traspaso_standard")]
    [InlineData("traspaso", "traspaso", "traspaso_standard")] // case-insensitive
    [InlineData("MATRICULAS", "matricula_inicial", "matricula_inicial")]
    [InlineData("OTROS", "matricula_inicial", "matricula_inicial")]
    [InlineData("UNKNOWN_FAMILY", "matricula_inicial", "matricula_inicial")] // default defensivo
    public async Task HandleAsync_SetsModalidadAndTipologiaFromFamily(
        string family, string expectedModalidad, string expectedTipologia)
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "X",
            Name = "X",
            Family = family,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _typeRepo.GetByIdAsync(Arg.Any<Guid>(), ct).Returns(pt);
        StubReferenceGenerator();

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _repo.Received(1).AddWithUniqueReferenceAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.ModalidadEntrada == expectedModalidad &&
                i.TipologiaCodigo == expectedTipologia),
            Arg.Any<int>(),
            ct);
    }
}
