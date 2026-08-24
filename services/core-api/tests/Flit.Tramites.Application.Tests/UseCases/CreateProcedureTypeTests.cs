using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases;

public sealed class CreateProcedureTypeTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();
    private readonly CreateProcedureTypeHandler _sut;

    public CreateProcedureTypeTests()
    {
        _sut = new CreateProcedureTypeHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_Returns201Summary()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateProcedureTypeRequest("MATRICULAS", "MAT_EST", "Matrícula Estándar", null);

        await _repo.AddAsync(Arg.Any<ProcedureType>(), ct);

        var (result, error) = await _sut.HandleAsync(request, ct);

        error.Should().BeNull();

        result.Should().NotBeNull();
        result!.Code.Should().Be("MAT_EST");
        result!.Family.Should().Be("MATRICULAS");
        result!.PublicationStatus.Should().Be(PublicationStatus.Draft);
        result!.IsActive.Should().BeTrue();
        result!.PublishedAt.Should().BeNull();

        await _repo.Received(1).AddAsync(Arg.Any<ProcedureType>(), ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task HandleAsync_AlwaysCreatesDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateProcedureTypeRequest("TRASPASO", "TRAS_001", "Traspaso", "Descripción");

        var (result, error) = await _sut.HandleAsync(request, ct);

        error.Should().BeNull();

        result!.PublicationStatus.Should().Be(PublicationStatus.Draft);
    }
}
