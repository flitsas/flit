using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureTypes;

/// <summary>
/// FEATURE-08 / HU-BE-08 (CFD-12) — listado de tipos publicados para el selector de operador.
/// Cubre BE-08-AC-01 (solo published), AC-04 (global) y AC-05 (id/code/name/family/version).
/// HU #12124 agrega cobertura de AC1/AC2: exposición de <c>Description</c> (copy del WIZARD).
/// </summary>
public sealed class GetPublishedProcedureTypesTests
{
    private readonly IProcedureTypeRepository _repo = Substitute.For<IProcedureTypeRepository>();

    [Fact]
    public async Task Handle_ReturnsPublishedTypesWithVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListAsync(null, PublicationStatus.Published, ct).Returns(new List<ProcedureType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TRASPASO_SIMPLE",
                Name = "Traspaso Simple",
                Family = "traspaso",
                Description = "Traspaso simple entre personas naturales sin gravámenes.",
                Version = 2,
                PublicationStatus = PublicationStatus.Published,
                WizardEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
        });

        var sut = new GetPublishedProcedureTypesHandler(_repo);
        var result = await sut.HandleAsync(ct);

        result.Should().ContainSingle();
        result[0].Id.Should().NotBeEmpty();
        result[0].Code.Should().Be("TRASPASO_SIMPLE");
        result[0].Name.Should().Be("Traspaso Simple");
        result[0].Family.Should().Be("traspaso");
        result[0].Version.Should().Be(2);
        // HU #12124 / AC1: el copy configurado en la entidad se expone tal cual en el DTO.
        result[0].Description.Should().Be("Traspaso simple entre personas naturales sin gravámenes.");
        // AC-01: la consulta se acota a 'published' en el repositorio.
        await _repo.Received(1).ListAsync(null, PublicationStatus.Published, ct);
    }

    [Fact]
    public async Task Handle_NoPublishedTypes_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.ListAsync(null, PublicationStatus.Published, ct).Returns(new List<ProcedureType>());

        var result = await new GetPublishedProcedureTypesHandler(_repo).HandleAsync(ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TypeWithoutDescription_ReturnsNullWithoutError()
    {
        // HU #12124 / AC2: si el tipo no tiene Description configurado, el DTO expone null
        // sin lanzar error.
        var ct = TestContext.Current.CancellationToken;
        _repo.ListAsync(null, PublicationStatus.Published, ct).Returns(new List<ProcedureType>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "CAMBIO_CHASIS",
                Name = "Cambio de Chasis",
                Family = "cambio",
                Description = null,
                Version = 1,
                PublicationStatus = PublicationStatus.Published,
                WizardEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow
            },
        });

        var sut = new GetPublishedProcedureTypesHandler(_repo);
        var result = await sut.HandleAsync(ct);

        result.Should().ContainSingle();
        result[0].Description.Should().BeNull();
    }
}
