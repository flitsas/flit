using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

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
                return Task.FromResult(AddProcedureInstanceOutcome.Created);
            });
    }

    private static CreateProcedureInstanceRequest Request() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

    private static CreateProcedureInstanceRequest ModalidadRequest(string? modalidad) =>
        new(Guid.NewGuid(), ProcedureTypeId: null, Guid.NewGuid(), null, Modalidad: modalidad);

    private static ProcedureType PublishedType(string code, string family) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Family = family,
        PublicationStatus = PublicationStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow
    };

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
        result.Status.Should().Be(TramiteEstado.Borrador);

        await _repo.Received(1).AddWithUniqueReferenceAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.Status == TramiteEstado.Borrador &&
                i.StatusHistory.Any(h => h.ToStatus == TramiteEstado.Borrador && h.FromStatus == null)),
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
            .Returns(Task.FromResult(AddProcedureInstanceOutcome.ReferenceConflict));

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().Be("reference_conflict");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ForeignKeyMissing_ReturnsInvalidReference()
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
        // tenant_id / created_by_user_id inexistente: el repo traduce la FK violation a
        // ReferencedEntityMissing → el handler responde "invalid_reference" (422, no 500).
        _repo.AddWithUniqueReferenceAsync(Arg.Any<ProcedureInstance>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AddProcedureInstanceOutcome.ReferencedEntityMissing));

        var (result, error) = await _sut.HandleAsync(Request(), ct);

        error.Should().Be("invalid_reference");
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

    [Fact]
    public async Task HandleAsync_ModalidadMatricula_ResolvesCanonicalPublishedMatriculaType()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = PublishedType("MATRICULA_NUEVA", "MATRICULAS");
        _typeRepo.GetByCodePublishedAsync("MATRICULA_NUEVA", ct).Returns(pt);
        StubReferenceGenerator();

        var (result, error) = await _sut.HandleAsync(ModalidadRequest("matricula_inicial"), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ProcedureTypeId.Should().Be(pt.Id);
        await _typeRepo.Received(1).GetByCodePublishedAsync("MATRICULA_NUEVA", ct);
        await _repo.Received(1).AddWithUniqueReferenceAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.ProcedureTypeId == pt.Id &&
                i.ModalidadEntrada == "matricula_inicial" &&
                i.TipologiaCodigo == "matricula_inicial"),
            Arg.Any<int>(),
            ct);
    }

    [Fact]
    public async Task HandleAsync_ModalidadTraspaso_ResolvesCanonicalPublishedTraspasoType()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = PublishedType("TRASPASO_STANDARD", "TRASPASO");
        _typeRepo.GetByCodePublishedAsync("TRASPASO_STANDARD", ct).Returns(pt);
        StubReferenceGenerator();

        var (result, error) = await _sut.HandleAsync(ModalidadRequest("traspaso"), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.ProcedureTypeId.Should().Be(pt.Id);
        await _repo.Received(1).AddWithUniqueReferenceAsync(
            Arg.Is<ProcedureInstance>(i =>
                i.ModalidadEntrada == "traspaso" &&
                i.TipologiaCodigo == "traspaso_standard"),
            Arg.Any<int>(),
            ct);
    }

    [Fact]
    public async Task HandleAsync_BothProcedureTypeIdAndModalidad_ReturnsInvalidRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateProcedureInstanceRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Modalidad: "matricula_inicial");

        var (result, error) = await _sut.HandleAsync(request, ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_NeitherProcedureTypeIdNorModalidad_ReturnsInvalidRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateProcedureInstanceRequest(
            Guid.NewGuid(), ProcedureTypeId: null, Guid.NewGuid(), null, Modalidad: null);

        var (result, error) = await _sut.HandleAsync(request, ct);

        error.Should().Be("invalid_request");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ModalidadNoPublishedType_ReturnsModalidadNotAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        _typeRepo.GetByCodePublishedAsync(Arg.Any<string>(), ct).Returns((ProcedureType?)null);

        var (result, error) = await _sut.HandleAsync(ModalidadRequest("matricula_inicial"), ct);

        error.Should().Be("modalidad_not_available");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnknownModalidad_ReturnsModalidadNotAvailable()
    {
        var ct = TestContext.Current.CancellationToken;

        var (result, error) = await _sut.HandleAsync(ModalidadRequest("no_existe"), ct);

        error.Should().Be("modalidad_not_available");
        result.Should().BeNull();
        await _typeRepo.DidNotReceive().GetByCodePublishedAsync(Arg.Any<string>(), ct);
    }
}
