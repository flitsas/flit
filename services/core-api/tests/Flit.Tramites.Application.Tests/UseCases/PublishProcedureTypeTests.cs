using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Services;
using Flit.Tramites.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases;

public sealed class PublishProcedureTypeTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();
    private readonly IProcedureTypeValidator _validator = Substitute.For<IProcedureTypeValidator>();
    private readonly PublishProcedureTypeHandler _sut;

    public PublishProcedureTypeTests()
    {
        _sut = new PublishProcedureTypeHandler(_repo, _validator);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ReturnsNotFoundError()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithDetailsAsync(Arg.Any<Guid>(), ct)
            .Returns((ProcedureType?)null);

        var (result, error, _) = await _sut.HandleAsync(Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ValidationFails_ReturnsValidationFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "X",
            Name = "X",
            Family = ProcedureFamilyCodes.Matriculas,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = []
        };

        _repo.GetByIdWithDetailsAsync(pt.Id, ct).Returns(pt);

        var failResult = new ValidationResult();
        failResult.AddError("VIN_PLATE_RULE", "Missing plate_or_vin", "steps.fields");
        _validator.Validate(pt).Returns(failResult);

        var (result, error, validationErrors) = await _sut.HandleAsync(pt.Id, ct);

        error.Should().Be("validation_failed");
        result.Should().BeNull();
        validationErrors.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_ValidProcedureType_SetsPublishedStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MAT_OK",
            Name = "Matrícula OK",
            Family = ProcedureFamilyCodes.Matriculas,
            PublicationStatus = PublicationStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = []
        };

        _repo.GetByIdWithDetailsAsync(pt.Id, ct).Returns(pt);
        _validator.Validate(pt).Returns(new ValidationResult());

        var (result, error, _) = await _sut.HandleAsync(pt.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PublicationStatus.Should().Be(PublicationStatus.Published);
        result.PublishedAt.Should().NotBeNull();
        await _repo.Received(1).UpdateAsync(
            Arg.Is<ProcedureType>(p => p.PublicationStatus == PublicationStatus.Published), ct);
    }

    [Fact]
    public async Task HandleAsync_FirstPublish_KeepsVersionAtOne()
    {
        // FEATURE-08 / BE-01-AC-07: la PRIMERA publicación (PublishedAt == null) conserva version=1.
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MAT_V1",
            Name = "Matrícula V1",
            Family = ProcedureFamilyCodes.Matriculas,
            PublicationStatus = PublicationStatus.Draft,
            Version = 1,
            PublishedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            ConformationRules = [],
            Steps = []
        };
        _repo.GetByIdWithDetailsAsync(pt.Id, ct).Returns(pt);
        _validator.Validate(pt).Returns(new ValidationResult());

        var (_, error, _) = await _sut.HandleAsync(pt.Id, ct);

        error.Should().BeNull();
        pt.Version.Should().Be(1);
        await _repo.Received(1).UpdateAsync(Arg.Is<ProcedureType>(p => p.Version == 1), ct);
    }

    [Fact]
    public async Task HandleAsync_RepublishEditedType_IncrementsVersion()
    {
        // FEATURE-08 / BE-01-AC-07: re-publicar un tipo YA publicado antes (PublishedAt != null, tras
        // volver a draft y editar) incrementa la versión semántica a 2. Las instancias en curso siguen
        // protegidas por su snapshot inmutable, no por esta versión live.
        var ct = TestContext.Current.CancellationToken;
        var pt = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = "MAT_V2",
            Name = "Matrícula V2",
            Family = ProcedureFamilyCodes.Matriculas,
            PublicationStatus = PublicationStatus.Draft,
            Version = 1,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ConformationRules = [],
            Steps = []
        };
        _repo.GetByIdWithDetailsAsync(pt.Id, ct).Returns(pt);
        _validator.Validate(pt).Returns(new ValidationResult());

        var (_, error, _) = await _sut.HandleAsync(pt.Id, ct);

        error.Should().BeNull();
        pt.Version.Should().Be(2);
        pt.PublicationStatus.Should().Be(PublicationStatus.Published);
        await _repo.Received(1).UpdateAsync(Arg.Is<ProcedureType>(p => p.Version == 2), ct);
    }
}
