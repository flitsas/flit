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
}
